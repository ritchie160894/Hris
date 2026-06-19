using System.Text;
using Hris.Api.Data;
using Hris.Api.Services;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Controllers;

/// <summary>
/// Reporting endpoints. Each report returns JSON rows for on-screen viewing;
/// append ?format=csv to download as CSV (opens in Excel). PDF export is done
/// client-side via the browser's print dialog from the report view.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Roles = ReportRoles)]
public class ReportsController(HrisDbContext db) : ControllerBase
{
    private const string ReportRoles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)},{nameof(UserRole.PayrollOfficer)},{nameof(UserRole.DepartmentHead)}";
    private const string PayrollReportRoles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.PayrollOfficer)}";
    private const string HrReportRoles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)}";
    private const string EmployeeReportRoles = $"{ReportRoles}";

    [HttpGet("attendance")]
    [Authorize(Roles = ReportRoles)]
    public async Task<IActionResult> Attendance(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] int? branchId, [FromQuery] int? departmentId, [FromQuery] string? employeeName,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? format = null)
    {
        var execIds = await ExecutiveExemption.GetExemptEmployeeIdsAsync(db);
        var q = db.AttendanceLogs
            .Include(a => a.Employee).ThenInclude(e => e!.Department)
            .Include(a => a.Site)
            .Where(a => a.AttendanceDate >= from && a.AttendanceDate <= to && !execIds.Contains(a.EmployeeId));

        if (branchId.HasValue) q = q.Where(a => a.Employee!.BranchId == branchId);
        if (departmentId.HasValue) q = q.Where(a => a.Employee!.DepartmentId == departmentId);
        if (!string.IsNullOrWhiteSpace(employeeName))
        {
            var term = employeeName.Trim();
            q = q.Where(a =>
                (a.Employee!.FirstName + " " + a.Employee.LastName).Contains(term) ||
                a.Employee.EmployeeCode.Contains(term));
        }

        if (User.Role() == UserRole.DepartmentHead)
        {
            var me = await db.Users.Include(u => u.Employee).FirstAsync(u => u.Id == User.UserId());
            q = q.Where(a => a.Employee!.DepartmentId == me.Employee!.DepartmentId);
        }

        if (format == "csv")
        {
            var rows = await q.OrderBy(a => a.Employee!.EmployeeCode).ThenBy(a => a.PunchTime)
                .Select(a => new
                {
                    EmployeeCode = a.Employee!.EmployeeCode,
                    Name = a.Employee.FirstName + " " + a.Employee.LastName,
                    Department = a.Employee.Department!.Name,
                    Date = a.AttendanceDate,
                    Time = a.PunchTime,
                    Punch = a.PunchType.ToString(),
                    Source = a.Source.ToString(),
                    Site = a.Site!.Name
                }).ToListAsync();

            return Csv("attendance-report", rows.Select(r => new Dictionary<string, object?>
            {
                ["Employee Code"] = r.EmployeeCode, ["Name"] = r.Name, ["Department"] = r.Department,
                ["Date"] = r.Date.ToString("yyyy-MM-dd"), ["Time"] = r.Time.ToString("HH:mm:ss"),
                ["Punch"] = r.Punch, ["Source"] = r.Source, ["Site"] = r.Site
            }));
        }

        var dayKeys = q.Select(a => new { a.EmployeeId, a.AttendanceDate });
        var total = await dayKeys.Distinct().CountAsync();
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        var pageDays = await dayKeys.Distinct()
            .OrderByDescending(d => d.AttendanceDate).ThenBy(d => d.EmployeeId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        if (pageDays.Count == 0)
            return Ok(new { total, page, pageSize, items = Array.Empty<object>() });

        var empIds = pageDays.Select(d => d.EmployeeId).Distinct().ToList();
        var dates = pageDays.Select(d => d.AttendanceDate).Distinct().ToList();
        var employees = await db.Employees.Include(e => e.Department)
            .Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        var rawLogs = await q
            .Where(a => empIds.Contains(a.EmployeeId) && dates.Contains(a.AttendanceDate))
            .OrderBy(a => a.PunchTime)
            .ToListAsync();

        var items = pageDays.Select(day =>
        {
            var dayLogs = rawLogs.Where(l => l.EmployeeId == day.EmployeeId && l.AttendanceDate == day.AttendanceDate).ToList();
            employees.TryGetValue(day.EmployeeId, out var emp);
            var slots = MapDayPunchTimes(dayLogs);
            var first = dayLogs.FirstOrDefault();
            return new
            {
                employeeCode = emp?.EmployeeCode,
                name = emp?.FullName,
                department = emp?.Department?.Name,
                date = day.AttendanceDate.ToString("yyyy-MM-dd"),
                morningIn = slots.morningIn,
                lunchOut = slots.lunchOut,
                afternoonIn = slots.afternoonIn,
                endOut = slots.endOut,
                source = first?.Source.ToString(),
                site = first?.Site?.Name,
                punchCount = dayLogs.Count
            };
        }).ToList();

        return Ok(new { total, page, pageSize, items });
    }

    private static (string? morningIn, string? lunchOut, string? afternoonIn, string? endOut) MapDayPunchTimes(List<AttendanceLog> logs)
    {
        var ordered = logs.OrderBy(l => l.PunchTime).ToList();
        var timeIns = ordered.Where(l => l.PunchType == PunchType.TimeIn).ToList();
        var timeOuts = ordered.Where(l => l.PunchType == PunchType.TimeOut).ToList();
        var breakOut = ordered.FirstOrDefault(l => l.PunchType == PunchType.BreakOut);
        var breakIn = ordered.FirstOrDefault(l => l.PunchType == PunchType.BreakIn);
        var noon = new TimeOnly(13, 0);

        var lunchOutLog = breakOut ?? timeOuts.FirstOrDefault(t => TimeOnly.FromDateTime(t.PunchTime) < noon);
        var endOutLog = timeOuts.LastOrDefault();
        if (lunchOutLog != null && endOutLog != null && lunchOutLog.Id == endOutLog.Id && timeOuts.Count == 1 && TimeOnly.FromDateTime(endOutLog.PunchTime) >= noon)
            lunchOutLog = null;

        static string? Fmt(AttendanceLog? l) => l is null ? null : l.PunchTime.ToString("HH:mm");
        return (Fmt(timeIns.FirstOrDefault()), Fmt(lunchOutLog), Fmt(breakIn ?? timeIns.Skip(1).FirstOrDefault()), Fmt(endOutLog));
    }

    [HttpGet("payroll")]
    [Authorize(Roles = PayrollReportRoles)]
    public async Task<IActionResult> Payroll([FromQuery] int cutoffId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? format = null)
    {
        if (cutoffId <= 0)
            return BadRequest(new { message = "Select a payroll cutoff." });

        var execIds = await ExecutiveExemption.GetExemptEmployeeIdsAsync(db);
        var q = db.Payslips.Include(p => p.Employee).ThenInclude(e => e!.Department)
            .Where(p => p.PayrollCutoffId == cutoffId && !execIds.Contains(p.EmployeeId))
            .OrderBy(p => p.Employee!.EmployeeCode);

        if (format == "csv")
        {
            var rows = await q.Select(p => new
            {
                EmployeeCode = p.Employee!.EmployeeCode,
                Name = p.Employee.FirstName + " " + p.Employee.LastName,
                Department = p.Employee.Department!.Name,
                p.BasicPay, p.OvertimePay, p.Allowances, p.GrossPay,
                Sss = p.SssEmployee, PhilHealth = p.PhilHealthEmployee, PagIbig = p.PagIbigEmployee,
                Tax = p.WithholdingTax, Loans = p.LoanDeductions, p.TotalDeductions, p.NetPay
            }).ToListAsync();

            return Csv("payroll-register", rows.Select(r => new Dictionary<string, object?>
            {
                ["Employee Code"] = r.EmployeeCode, ["Name"] = r.Name, ["Department"] = r.Department,
                ["Basic Pay"] = r.BasicPay, ["OT Pay"] = r.OvertimePay, ["Allowances"] = r.Allowances,
                ["Gross"] = r.GrossPay, ["SSS"] = r.Sss, ["PhilHealth"] = r.PhilHealth, ["Pag-IBIG"] = r.PagIbig,
                ["Tax"] = r.Tax, ["Loans"] = r.Loans, ["Total Deductions"] = r.TotalDeductions, ["Net Pay"] = r.NetPay
            }));
        }

        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new
            {
                employeeCode = p.Employee!.EmployeeCode,
                name = p.Employee.FirstName + " " + p.Employee.LastName,
                department = p.Employee.Department!.Name,
                basicPay = p.BasicPay,
                overtimePay = p.OvertimePay,
                allowances = p.Allowances,
                grossPay = p.GrossPay,
                sss = p.SssEmployee,
                philHealth = p.PhilHealthEmployee,
                pagIbig = p.PagIbigEmployee,
                tax = p.WithholdingTax,
                loans = p.LoanDeductions,
                totalDeductions = p.TotalDeductions,
                netPay = p.NetPay
            }).ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("leave")]
    [Authorize(Roles = ReportRoles)]
    public async Task<IActionResult> Leave([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? format = null)
    {
        var q = db.LeaveRequests.Include(l => l.Employee).ThenInclude(e => e!.Department).Include(l => l.LeaveType)
            .Where(l => l.StartDate >= from && l.StartDate <= to)
            .OrderByDescending(l => l.StartDate);

        if (format == "csv")
        {
            var rows = await q.OrderBy(l => l.StartDate)
                .Select(l => new
                {
                    EmployeeCode = l.Employee!.EmployeeCode,
                    Name = l.Employee.FirstName + " " + l.Employee.LastName,
                    Department = l.Employee.Department!.Name,
                    LeaveType = l.LeaveType!.Name,
                    l.StartDate, l.EndDate, l.Days,
                    Status = l.Status.ToString(), l.Reason
                }).ToListAsync();

            return Csv("leave-report", rows.Select(r => new Dictionary<string, object?>
            {
                ["Employee Code"] = r.EmployeeCode, ["Name"] = r.Name, ["Department"] = r.Department,
                ["Leave Type"] = r.LeaveType, ["Start"] = r.StartDate.ToString("yyyy-MM-dd"),
                ["End"] = r.EndDate.ToString("yyyy-MM-dd"), ["Days"] = r.Days, ["Status"] = r.Status, ["Reason"] = r.Reason
            }));
        }

        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new
            {
                employeeCode = l.Employee!.EmployeeCode,
                name = l.Employee.FirstName + " " + l.Employee.LastName,
                department = l.Employee.Department!.Name,
                leaveType = l.LeaveType!.Name,
                startDate = l.StartDate.ToString("yyyy-MM-dd"),
                endDate = l.EndDate.ToString("yyyy-MM-dd"),
                days = l.Days,
                status = l.Status.ToString(),
                reason = l.Reason
            }).ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("overtime")]
    [Authorize(Roles = ReportRoles)]
    public async Task<IActionResult> Overtime([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? format = null)
    {
        var q = db.OvertimeRequests.Include(o => o.Employee).ThenInclude(e => e!.Department)
            .Where(o => o.OvertimeDate >= from && o.OvertimeDate <= to)
            .OrderByDescending(o => o.OvertimeDate);

        if (format == "csv")
        {
            var rows = await q.OrderBy(o => o.OvertimeDate)
                .Select(o => new
                {
                    EmployeeCode = o.Employee!.EmployeeCode,
                    Name = o.Employee.FirstName + " " + o.Employee.LastName,
                    Department = o.Employee.Department!.Name,
                    Date = o.OvertimeDate, o.Hours, Status = o.Status.ToString(), Pay = o.ComputedPay, o.Reason
                }).ToListAsync();

            return Csv("overtime-report", rows.Select(r => new Dictionary<string, object?>
            {
                ["Employee Code"] = r.EmployeeCode, ["Name"] = r.Name, ["Department"] = r.Department,
                ["Date"] = r.Date.ToString("yyyy-MM-dd"), ["Hours"] = r.Hours, ["Status"] = r.Status,
                ["Computed Pay"] = r.Pay, ["Reason"] = r.Reason
            }));
        }

        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new
            {
                employeeCode = o.Employee!.EmployeeCode,
                name = o.Employee.FirstName + " " + o.Employee.LastName,
                department = o.Employee.Department!.Name,
                date = o.OvertimeDate.ToString("yyyy-MM-dd"),
                hours = o.Hours,
                status = o.Status.ToString(),
                pay = o.ComputedPay,
                reason = o.Reason
            }).ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("employees")]
    [Authorize(Roles = EmployeeReportRoles)]
    public async Task<IActionResult> Employees([FromQuery] string? format)
    {
        var rows = await db.Employees.Include(e => e.Department).Include(e => e.Position).Include(e => e.Branch)
            .OrderBy(e => e.EmployeeCode)
            .Select(e => new
            {
                e.EmployeeCode,
                Name = e.FirstName + " " + e.LastName,
                Department = e.Department!.Name, Position = e.Position!.Title, Branch = e.Branch!.Name,
                Status = e.Status.ToString(), e.HireDate, e.MonthlySalary,
                e.SssNumber, e.PhilHealthNumber, e.PagIbigNumber, e.Tin
            }).ToListAsync();

        return format == "csv"
            ? Csv("employee-masterlist", rows.Select(r => new Dictionary<string, object?>
              {
                  ["Employee Code"] = r.EmployeeCode, ["Name"] = r.Name, ["Department"] = r.Department,
                  ["Position"] = r.Position, ["Branch"] = r.Branch, ["Status"] = r.Status,
                  ["Hire Date"] = r.HireDate.ToString("yyyy-MM-dd"), ["Monthly Salary"] = r.MonthlySalary,
                  ["SSS"] = r.SssNumber, ["PhilHealth"] = r.PhilHealthNumber, ["Pag-IBIG"] = r.PagIbigNumber, ["TIN"] = r.Tin
              }))
            : Ok(rows);
    }

    [HttpGet("government")]
    [Authorize(Roles = PayrollReportRoles)]
    public async Task<IActionResult> Government(
        [FromQuery] int year, [FromQuery] int month,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? format = null)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var execIds = await ExecutiveExemption.GetExemptEmployeeIdsAsync(db);

        // One row per employee: sum statutory amounts from finalized cutoffs in the month.
        // SSS/PhilHealth/Pag-IBIG are deducted on the 2nd cutoff; tax on the 1st — no double-count.
        var q = db.Payslips.AsNoTracking()
            .Where(p => !execIds.Contains(p.EmployeeId))
            .Where(p => p.PayrollCutoff != null
                && p.PayrollCutoff.PeriodStart >= first
                && p.PayrollCutoff.PeriodEnd <= last
                && (p.PayrollCutoff.Status == PayrollStatus.Approved
                    || p.PayrollCutoff.Status == PayrollStatus.Released
                    || p.PayrollCutoff.Status == PayrollStatus.Closed))
            .GroupBy(p => new { p.EmployeeId, p.Employee!.EmployeeCode, p.Employee.FirstName, p.Employee.LastName })
            .Select(g => new
            {
                employeeCode = g.Key.EmployeeCode,
                name = g.Key.FirstName + " " + g.Key.LastName,
                sssEe = g.Sum(p => p.SssEmployee),
                sssEr = g.Sum(p => p.SssEmployer),
                philHealth = g.Sum(p => p.PhilHealthEmployee + p.PhilHealthEmployer),
                pagIbig = g.Sum(p => p.PagIbigEmployee + p.PagIbigEmployer),
                tax = g.Sum(p => p.WithholdingTax)
            })
            .OrderBy(r => r.employeeCode);

        if (format == "csv")
        {
            var rows = await q.ToListAsync();
            return Csv("government-remittance", rows.Select(r => new Dictionary<string, object?>
            {
                ["Employee Code"] = r.employeeCode, ["Name"] = r.name,
                ["SSS EE"] = r.sssEe, ["SSS ER"] = r.sssEr,
                ["PhilHealth Total"] = r.philHealth, ["Pag-IBIG Total"] = r.pagIbig,
                ["Withholding Tax"] = r.tax
            }));
        }

        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("branches")]
    [Authorize(Roles = HrReportRoles)]
    public async Task<IActionResult> Branches([FromQuery] string? format)
    {
        var rows = await db.Branches
            .Select(b => new
            {
                Branch = b.Name,
                Sites = b.Sites.Count,
                Employees = db.Employees.Count(e => e.BranchId == b.Id),
                Devices = db.BiometricDevices.Count(d => d.Site!.BranchId == b.Id),
                Active = b.IsActive
            }).ToListAsync();

        return format == "csv"
            ? Csv("branch-report", rows.Select(r => new Dictionary<string, object?>
              {
                  ["Branch"] = r.Branch, ["Sites"] = r.Sites, ["Employees"] = r.Employees,
                  ["Devices"] = r.Devices, ["Active"] = r.Active
              }))
            : Ok(rows);
    }

    private FileContentResult Csv(string name, IEnumerable<Dictionary<string, object?>> rows)
    {
        var sb = new StringBuilder();
        var list = rows.ToList();
        if (list.Count > 0)
        {
            sb.AppendLine(string.Join(",", list[0].Keys.Select(Escape)));
            foreach (var row in list)
                sb.AppendLine(string.Join(",", row.Values.Select(v => Escape(v?.ToString() ?? ""))));
        }
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),
            "text/csv", $"{name}-{DateTime.Now:yyyyMMdd-HHmm}.csv");

        static string Escape(string s) => s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
