using Hris.Api.Data;
using Hris.Api.Services;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Controllers;

[ApiController]
[Route("api/attendance")]
[Authorize]
public class AttendanceController(HrisDbContext db, AuditService audit, ApprovalService approvals, NotificationService notifications, AttendanceSummaryService summaries, DashboardService dashboard) : ControllerBase
{
    [HttpGet("logs")]
    public async Task<IActionResult> Logs([FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] int? employeeId, [FromQuery] int? siteId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var q = db.AttendanceLogs.Include(a => a.Employee).Include(a => a.Site).Include(a => a.Device).AsQueryable();

        // employees see only their own logs
        if (User.Role() == UserRole.Employee)
            q = q.Where(a => a.EmployeeId == User.EmployeeId());
        else if (User.Role() == UserRole.DepartmentHead)
        {
            var me = await db.Users.Include(u => u.Employee).FirstAsync(u => u.Id == User.UserId());
            q = q.Where(a => a.Employee!.DepartmentId == me.Employee!.DepartmentId);
        }

        if (from.HasValue) q = q.Where(a => a.AttendanceDate >= from.Value);
        if (to.HasValue) q = q.Where(a => a.AttendanceDate <= to.Value);
        if (employeeId.HasValue) q = q.Where(a => a.EmployeeId == employeeId);
        if (siteId.HasValue) q = q.Where(a => a.SiteId == siteId);

        if (User.Role() != UserRole.Employee)
        {
            var execIds = await ExecutiveExemption.GetExemptEmployeeIdsAsync(db);
            q = q.Where(a => !execIds.Contains(a.EmployeeId));
        }

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(a => a.PunchTime)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new
            {
                a.Id, a.PunchTime, punchType = a.PunchType.ToString(), source = a.Source.ToString(),
                a.VerifyMode, a.IsCorrected, a.Remarks,
                employee = new { a.Employee!.Id, a.Employee.EmployeeCode, name = a.Employee.FirstName + " " + a.Employee.LastName },
                site = a.Site!.Name, device = a.Device != null ? a.Device.Name : null
            })
            .ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    /// <summary>One row per employee-day — four punch slots (8 AM in, 12 NN out, 1 PM in, 5 PM out).</summary>
    [HttpGet("logs/by-day")]
    public async Task<IActionResult> LogsByDay([FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] int? employeeId, [FromQuery] int? siteId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var q = db.AttendanceLogs.AsQueryable();

        if (User.Role() == UserRole.Employee)
            q = q.Where(a => a.EmployeeId == User.EmployeeId());
        else if (User.Role() == UserRole.DepartmentHead)
        {
            var me = await db.Users.Include(u => u.Employee).FirstAsync(u => u.Id == User.UserId());
            q = q.Where(a => a.Employee!.DepartmentId == me.Employee!.DepartmentId);
        }

        if (from.HasValue) q = q.Where(a => a.AttendanceDate >= from.Value);
        if (to.HasValue) q = q.Where(a => a.AttendanceDate <= to.Value);
        if (employeeId.HasValue) q = q.Where(a => a.EmployeeId == employeeId);
        if (siteId.HasValue) q = q.Where(a => a.SiteId == siteId);

        if (User.Role() != UserRole.Employee)
        {
            var execIds = await ExecutiveExemption.GetExemptEmployeeIdsAsync(db);
            q = q.Where(a => !execIds.Contains(a.EmployeeId));
        }

        var dayKeys = q.Select(a => new { a.EmployeeId, a.AttendanceDate });
        var total = await dayKeys.Distinct().CountAsync();

        var pageDays = await dayKeys.Distinct()
            .OrderByDescending(d => d.AttendanceDate).ThenByDescending(d => d.EmployeeId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        if (pageDays.Count == 0)
            return Ok(new { total, page, pageSize, items = Array.Empty<object>() });

        var empIds = pageDays.Select(d => d.EmployeeId).Distinct().ToList();
        var dates = pageDays.Select(d => d.AttendanceDate).Distinct().ToList();
        var employees = await db.Employees.Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.EmployeeCode, name = e.FirstName + " " + e.LastName })
            .ToDictionaryAsync(e => e.Id);

        var rawLogs = await db.AttendanceLogs.Include(a => a.Site).Include(a => a.Device)
            .Where(a => empIds.Contains(a.EmployeeId) && dates.Contains(a.AttendanceDate))
            .OrderBy(a => a.PunchTime)
            .ToListAsync();

        var items = pageDays.Select(day =>
        {
            var dayLogs = rawLogs.Where(l => l.EmployeeId == day.EmployeeId && l.AttendanceDate == day.AttendanceDate).ToList();
            employees.TryGetValue(day.EmployeeId, out var emp);
            var first = dayLogs.FirstOrDefault();
            return new
            {
                date = day.AttendanceDate,
                employee = emp,
                site = first?.Site?.Name,
                device = first?.Device?.Name,
                punches = MapDayPunches(dayLogs)
            };
        }).ToList();

        return Ok(new { total, page, pageSize, items });
    }

    private static IEnumerable<object> MapDayPunches(List<AttendanceLog> logs)
    {
        var ordered = logs.OrderBy(l => l.PunchTime).ToList();
        var timeIns = ordered.Where(l => l.PunchType == PunchType.TimeIn).ToList();
        var timeOuts = ordered.Where(l => l.PunchType == PunchType.TimeOut).ToList();
        var breakOut = ordered.FirstOrDefault(l => l.PunchType == PunchType.BreakOut);
        var breakIn = ordered.FirstOrDefault(l => l.PunchType == PunchType.BreakIn);
        var noon = new TimeOnly(13, 0);

        var lunchOut = breakOut ?? timeOuts.FirstOrDefault(t => TimeOnly.FromDateTime(t.PunchTime) < noon);
        var endOut = timeOuts.LastOrDefault();
        if (lunchOut != null && endOut != null && lunchOut.Id == endOut.Id && timeOuts.Count == 1 && TimeOnly.FromDateTime(endOut.PunchTime) >= noon)
            lunchOut = null;

        var slots = new (string slot, string label, AttendanceLog? log)[]
        {
            ("morningIn", "Time In", timeIns.FirstOrDefault()),
            ("lunchOut", "Time Out", lunchOut),
            ("afternoonIn", "Time In", breakIn ?? timeIns.Skip(1).FirstOrDefault()),
            ("endOut", "Time Out", endOut)
        };

        return slots.Select(s => s.log is null
            ? new { s.slot, s.label, punchTime = (DateTime?)null, source = (string?)null, verifyMode = (string?)null, site = (string?)null, device = (string?)null, missing = true, isCorrected = false }
            : new
            {
                s.slot, s.label,
                punchTime = (DateTime?)s.log.PunchTime,
                source = s.log.Source.ToString(),
                verifyMode = s.log.VerifyMode,
                site = s.log.Site?.Name,
                device = s.log.Device?.Name,
                missing = false,
                isCorrected = s.log.IsCorrected
            });
    }

    /// <summary>Daily summary per employee — reads pre-aggregated AttendanceDailySummaries (fast at scale).</summary>
    [HttpGet("daily-summary")]
    public async Task<IActionResult> DailySummary([FromQuery] DateOnly date, [FromQuery] int? departmentId)
    {
        var execIds = await ExecutiveExemption.GetExemptEmployeeIdsAsync(db);

        var employeesQ = db.Employees
            .Where(e => e.Status != EmploymentStatus.Resigned && e.Status != EmploymentStatus.Terminated)
            .Where(e => !execIds.Contains(e.Id));
        if (departmentId.HasValue) employeesQ = employeesQ.Where(e => e.DepartmentId == departmentId);
        if (User.Role() == UserRole.DepartmentHead)
        {
            var me = await db.Users.Include(u => u.Employee).FirstAsync(u => u.Id == User.UserId());
            employeesQ = employeesQ.Where(e => e.DepartmentId == me.Employee!.DepartmentId);
        }

        var employees = await employeesQ.Include(e => e.Department).ToListAsync();
        var employeeIds = employees.Select(e => e.Id).ToList();
        await summaries.EnsureRangeAsync(date, date, employeeIds);

        var daySummaries = await db.AttendanceDailySummaries
            .Where(s => s.AttendanceDate == date && employeeIds.Contains(s.EmployeeId))
            .ToDictionaryAsync(s => s.EmployeeId);

        var rows = employees.Select(e =>
        {
            daySummaries.TryGetValue(e.Id, out var s);
            var status = s?.Status switch
            {
                "OnLeave" => "On Leave",
                "RestDay" => "Rest Day",
                _ => s?.Status ?? "Absent"
            };
            return new
            {
                e.Id, e.EmployeeCode, name = e.FullName, department = e.Department?.Name,
                timeIn = s?.TimeIn, timeOut = s?.TimeOut, breakOut = s?.BreakOut, breakIn = s?.BreakIn,
                lateMins = s?.LateMinutes > 0 ? (double?)s.LateMinutes : null,
                hours = s?.HoursWorked > 0 ? (double?)s.HoursWorked : null,
                status
            };
        });
        return Ok(rows);
    }

    /// <summary>Manual attendance entry (HR only).</summary>
    [HttpPost("logs")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)}")]
    public async Task<IActionResult> AddManual([FromBody] ManualLogRequest req)
    {
        var exists = await db.AttendanceLogs.AnyAsync(a =>
            a.EmployeeId == req.EmployeeId && a.PunchTime == req.PunchTime && a.PunchType == req.PunchType);
        if (exists) return BadRequest(new { message = "An identical punch already exists." });

        var employee = await db.Employees.FindAsync(req.EmployeeId);
        if (employee is null) return NotFound(new { message = "Employee not found." });
        if (await ExecutiveExemption.IsExemptAsync(db, req.EmployeeId))
            return BadRequest(new { message = "Executives (VP & CEO) are exempt from attendance timekeeping." });

        var log = new AttendanceLog
        {
            EmployeeId = req.EmployeeId, PunchTime = req.PunchTime, PunchType = req.PunchType,
            Source = AttendanceSource.Manual, SiteId = employee.SiteId, Remarks = req.Remarks
        };
        AttendanceSummaryService.StampLog(log);
        db.AttendanceLogs.Add(log);
        audit.Log(AuditCategory.RecordChange, $"Manual attendance entry for employee #{req.EmployeeId}", nameof(AttendanceLog), null, $"{req.PunchType} at {req.PunchTime:yyyy-MM-dd HH:mm}");
        await db.SaveChangesAsync();
        await summaries.UpsertDailyAsync(req.EmployeeId, log.AttendanceDate);
        await db.SaveChangesAsync();
        dashboard.Invalidate();
        return Ok(log);
    }

    public record ManualLogRequest(int EmployeeId, DateTime PunchTime, PunchType PunchType, string? Remarks);

    // ---- Corrections ----
    [HttpGet("corrections")]
    public async Task<IActionResult> Corrections([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var q = db.AttendanceCorrections.Include(c => c.Employee).AsQueryable();
        if (User.Role() == UserRole.Employee) q = q.Where(c => c.EmployeeId == User.EmployeeId());
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<RequestStatus>(status, out var st)) q = q.Where(c => c.Status == st);

        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new
            {
                c.Id, c.AttendanceDate, punchType = c.PunchType.ToString(),
                issueType = c.IssueType.ToString(), c.OriginalTime, c.CorrectedTime,
                c.Reason, c.SupportingDocument, status = c.Status.ToString(),
                c.CurrentApprovalLevel, c.PayrollAppliedAt, c.ApproverRemarks, c.CreatedAt,
                employee = new { c.Employee!.Id, c.Employee.EmployeeCode, name = c.Employee.FirstName + " " + c.Employee.LastName }
            }).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpPost("corrections")]
    public async Task<IActionResult> SubmitCorrection(AttendanceCorrection input)
    {
        var eid = User.EmployeeId();
        if (User.Role() == UserRole.Employee && input.EmployeeId != eid)
            return Forbid();
        if (await ExecutiveExemption.IsExemptAsync(db, input.EmployeeId))
            return BadRequest(new { message = "Executives (VP & CEO) are exempt from attendance timekeeping." });
        input.Id = 0;
        input.Status = RequestStatus.Pending;
        input.CurrentApprovalLevel = 1;
        input.RequestedByUserId = User.UserId();
        if (input.OriginalTime is null)
        {
            var dayStart = input.AttendanceDate.ToDateTime(TimeOnly.MinValue);
            var dayEnd = input.AttendanceDate.ToDateTime(TimeOnly.MaxValue);
            input.OriginalTime = await db.AttendanceLogs
                .Where(a => a.EmployeeId == input.EmployeeId && a.PunchType == input.PunchType
                    && a.PunchTime >= dayStart && a.PunchTime <= dayEnd)
                .Select(a => (DateTime?)a.PunchTime)
                .FirstOrDefaultAsync();
        }
        db.AttendanceCorrections.Add(input);
        await db.SaveChangesAsync();
        await approvals.InitializeChainAsync(RequestType.AttendanceCorrection, input.Id, input.EmployeeId,
            $"{input.IssueType}: {input.AttendanceDate:yyyy-MM-dd} ({input.PunchType})");
        await db.SaveChangesAsync();
        return Ok(input);
    }

    [HttpPost("corrections/{id:int}/act")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)},{nameof(UserRole.DepartmentHead)},{nameof(UserRole.VicePresidentHrHead)},{nameof(UserRole.PayrollOfficer)}")]
    public async Task<IActionResult> ActOnCorrection(int id, [FromBody] ActRequest req)
    {
        var c = await db.AttendanceCorrections.FindAsync(id);
        if (c is null) return NotFound();

        var pendingStep = await db.ApprovalActions
            .FirstOrDefaultAsync(a => a.RequestType == RequestType.AttendanceCorrection
                && a.RequestId == id && a.Status == ApprovalStepStatus.Pending);
        if (pendingStep is null) return BadRequest(new { message = "No pending approval step." });

        var template = await db.WorkflowTemplateSteps
            .FirstOrDefaultAsync(s => s.RequestType == RequestType.AttendanceCorrection && s.Level == pendingStep.Level);
        if (template?.IsApplyOnlyStep == true && !req.Approve)
            return BadRequest(new { message = "Payroll can only apply approved corrections — use Apply to acknowledge." });

        var result = await approvals.ActAsync(RequestType.AttendanceCorrection, id, User.UserId(), User.DisplayName(), User.Role(), req.Approve, req.Remarks, req.Return);
        c.Status = result.Status;
        c.CurrentApprovalLevel = result.NextLevel;
        c.ActedAt = DateTime.UtcNow;
        c.ApprovedByUserId = User.UserId();
        c.ApproverRemarks = req.Remarks;

        if (req.Approve && CorrectionWorkflowHelper.IsHrFinalApprovalStep(RequestType.AttendanceCorrection, pendingStep.Level))
        {
            await CorrectionWorkflowHelper.ApplyAttendanceCorrectionAsync(db, c);
            await summaries.UpsertDailyAsync(c.EmployeeId, c.AttendanceDate);
            dashboard.Invalidate();
        }

        if (result.Completed && result.Status == RequestStatus.Approved && template?.IsApplyOnlyStep == true)
        {
            c.PayrollAppliedAt = DateTime.UtcNow;
        }

        await notifications.NotifyEmployeeAsync(c.EmployeeId, NotificationType.ApprovalResult,
            $"Attendance correction {result.Status}",
            $"Your correction for {c.AttendanceDate:MMM d} is now {result.Status}.");
        await db.SaveChangesAsync();
        return Ok(new { status = c.Status.ToString(), payrollApplied = c.PayrollAppliedAt != null });
    }

    public record ActRequest(bool Approve, string? Remarks, bool Return = false);
}
