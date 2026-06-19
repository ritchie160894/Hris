using Hris.Api.Data;
using Hris.Api.Services;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Controllers;

/// <summary>
/// Executive Approval Portal endpoints. Aggregates pending requests across
/// Leave, SIL, Overtime, Cash Advance and Loan workflows for the current approver.
/// </summary>
[ApiController]
[Route("api/approvals")]
[Authorize]
public class ApprovalsController(HrisDbContext db, ApprovalService approvals, NotificationService notifications, AuditService audit) : ControllerBase
{
    private static readonly UserRole[] ApproverRoles =
    [
        UserRole.SuperAdministrator, UserRole.DepartmentHead, UserRole.HrOfficer,
        UserRole.VicePresidentHrHead, UserRole.PresidentCeo, UserRole.HrAdministrator,
        UserRole.Supervisor, UserRole.PayrollOfficer
    ];

    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
    {
        var role = User.Role();
        if (!ApproverRoles.Contains(role)) return Ok(Array.Empty<object>());

        // Steps currently pending for my role
        var stepsQ = db.ApprovalActions.Where(a => a.Status == ApprovalStepStatus.Pending);
        if (role != UserRole.SuperAdministrator)
            stepsQ = stepsQ.Where(a => a.ApproverRole == role);
        var steps = await stepsQ.ToListAsync();

        int? myDepartmentId = null;
        if (role == UserRole.DepartmentHead)
        {
            var me = await db.Users.Include(u => u.Employee).FirstAsync(u => u.Id == User.UserId());
            myDepartmentId = me.Employee?.DepartmentId;
        }

        var result = new List<object>();
        var workflowTemplates = await db.WorkflowTemplateSteps.AsNoTracking().ToListAsync();

        foreach (var group in steps.GroupBy(s => s.RequestType))
        {
            var ids = group.Select(s => s.RequestId).ToList();
            switch (group.Key)
            {
                case RequestType.Leave:
                case RequestType.ServiceIncentiveLeave:
                    var leaves = await db.LeaveRequests.Include(l => l.Employee).ThenInclude(e => e!.Department).Include(l => l.LeaveType)
                        .Where(l => ids.Contains(l.Id) && (l.Status == RequestStatus.Pending || l.Status == RequestStatus.InProgress))
                        .ToListAsync();
                    if (myDepartmentId.HasValue) leaves = leaves.Where(l => l.Employee?.DepartmentId == myDepartmentId).ToList();
                    result.AddRange(leaves.Select(l =>
                    {
                        var kind = l.IsUndertime ? "undertime" : l.IsHalfDay ? "half day" : "leave";
                        var utPart = l.IsUndertime ? $" ({l.UndertimeHours:n2} hr/s)" : "";
                        return new
                        {
                            requestType = group.Key.ToString(),
                            typeLabel = group.Key == RequestType.ServiceIncentiveLeave ? "SIL" : "EL",
                            requestId = l.Id,
                            employee = l.Employee?.FullName,
                            employeeCode = l.Employee?.EmployeeCode,
                            department = l.Employee?.Department?.Name,
                            summary = $"{l.LeaveType?.Code} {kind}{utPart}: {l.StartDate:MMM d} - {l.EndDate:MMM d} ({l.Days} day/s)",
                            details = l.Reason,
                            date = l.CreatedAt,
                            level = group.First(s => s.RequestId == l.Id).Level,
                            stepName = group.First(s => s.RequestId == l.Id).StepName
                        };
                    }));
                    break;
                case RequestType.Overtime:
                    var ots = await db.OvertimeRequests.Include(o => o.Employee).ThenInclude(e => e!.Department)
                        .Where(o => ids.Contains(o.Id) && (o.Status == RequestStatus.Pending || o.Status == RequestStatus.InProgress))
                        .ToListAsync();
                    if (myDepartmentId.HasValue) ots = ots.Where(o => o.Employee?.DepartmentId == myDepartmentId).ToList();
                    result.AddRange(ots.Select(o => new
                    {
                        requestType = group.Key.ToString(),
                        typeLabel = "Overtime",
                        requestId = o.Id,
                        employee = o.Employee?.FullName,
                        employeeCode = o.Employee?.EmployeeCode,
                        department = o.Employee?.Department?.Name,
                        summary = $"{o.OvertimeDate:MMM d}: {o.StartTime:HH\\:mm} - {o.EndTime:HH\\:mm} ({o.Hours} hr/s)",
                        details = o.Reason,
                        date = o.CreatedAt,
                        level = group.First(s => s.RequestId == o.Id).Level,
                        stepName = group.First(s => s.RequestId == o.Id).StepName
                    }));
                    break;
                case RequestType.CashAdvance:
                case RequestType.Loan:
                    var loans = await db.Loans.Include(l => l.Employee).ThenInclude(e => e!.Department)
                        .Where(l => ids.Contains(l.Id) && (l.ApprovalStatus == RequestStatus.Pending || l.ApprovalStatus == RequestStatus.InProgress))
                        .ToListAsync();
                    if (myDepartmentId.HasValue) loans = loans.Where(l => l.Employee?.DepartmentId == myDepartmentId).ToList();
                    result.AddRange(loans.Select(l => new
                    {
                        requestType = group.Key.ToString(),
                        typeLabel = l.Type == LoanType.CashAdvance ? "Cash Advance" : "Loan",
                        requestId = l.Id,
                        employee = l.Employee?.FullName,
                        employeeCode = l.Employee?.EmployeeCode,
                        department = l.Employee?.Department?.Name,
                        summary = $"{l.Type}: ₱{l.Principal:n2} ({l.AmortizationPerCutoff:n2}/cutoff)",
                        details = l.Purpose,
                        date = l.CreatedAt,
                        level = group.First(s => s.RequestId == l.Id).Level,
                        stepName = group.First(s => s.RequestId == l.Id).StepName
                    }));
                    break;
                case RequestType.AttendanceCorrection:
                    var corrections = await db.AttendanceCorrections.Include(c => c.Employee).ThenInclude(e => e!.Department)
                        .Where(c => ids.Contains(c.Id) && (c.Status == RequestStatus.Pending || c.Status == RequestStatus.InProgress))
                        .ToListAsync();
                    if (myDepartmentId.HasValue) corrections = corrections.Where(c => c.Employee?.DepartmentId == myDepartmentId).ToList();
                    result.AddRange(corrections.Select(c =>
                    {
                        var level = group.First(s => s.RequestId == c.Id).Level;
                        return new
                        {
                            requestType = group.Key.ToString(),
                            typeLabel = "Attendance Correction",
                            requestId = c.Id,
                            employee = c.Employee?.FullName,
                            employeeCode = c.Employee?.EmployeeCode,
                            department = c.Employee?.Department?.Name,
                            summary = $"{c.IssueType}: {c.AttendanceDate:MMM d} {c.PunchType} → {c.CorrectedTime:HH:mm}",
                            details = c.Reason,
                            date = c.CreatedAt,
                            level,
                            stepName = group.First(s => s.RequestId == c.Id).StepName,
                            isApplyOnly = workflowTemplates.Any(t => t.RequestType == RequestType.AttendanceCorrection && t.Level == level && t.IsApplyOnlyStep)
                        };
                    }));
                    break;
                case RequestType.OvertimeCorrection:
                    var otCorrections = await db.OvertimeCorrections.Include(o => o.Employee).ThenInclude(e => e!.Department)
                        .Where(o => ids.Contains(o.Id) && (o.Status == RequestStatus.Pending || o.Status == RequestStatus.InProgress))
                        .ToListAsync();
                    if (myDepartmentId.HasValue) otCorrections = otCorrections.Where(o => o.Employee?.DepartmentId == myDepartmentId).ToList();
                    result.AddRange(otCorrections.Select(o =>
                    {
                        var level = group.First(s => s.RequestId == o.Id).Level;
                        return new
                        {
                            requestType = group.Key.ToString(),
                            typeLabel = "Overtime Correction",
                            requestId = o.Id,
                            employee = o.Employee?.FullName,
                            employeeCode = o.Employee?.EmployeeCode,
                            department = o.Employee?.Department?.Name,
                            summary = $"{o.IssueType}: {o.OvertimeDate:MMM d} ({o.Hours} hr/s)",
                            details = o.Reason,
                            date = o.CreatedAt,
                            level,
                            stepName = group.First(s => s.RequestId == o.Id).StepName,
                            isApplyOnly = workflowTemplates.Any(t => t.RequestType == RequestType.OvertimeCorrection && t.Level == level && t.IsApplyOnlyStep)
                        };
                    }));
                    break;
            }
        }

        return Ok(result.OrderBy(r => ((dynamic)r).date).ToList());
    }

    public record ActRequest(string RequestType, int RequestId, bool Approve, string? Remarks, bool ReturnForRevision = false);

    [HttpPost("act")]
    public async Task<IActionResult> Act(ActRequest req)
    {
        if (!Enum.TryParse<RequestType>(req.RequestType, out var type))
            return BadRequest(new { message = "Unknown request type." });

        ApprovalService.ApprovalResult result;
        try
        {
            result = await approvals.ActAsync(type, req.RequestId, User.UserId(), User.DisplayName(), User.Role(),
                req.Approve, req.Remarks, req.ReturnForRevision);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        // update underlying record + apply side effects
        switch (type)
        {
            case RequestType.Leave:
            case RequestType.ServiceIncentiveLeave:
            {
                var l = await db.LeaveRequests.Include(x => x.LeaveType).FirstAsync(x => x.Id == req.RequestId);
                l.Status = result.Status;
                l.CurrentApprovalLevel = result.NextLevel;
                if (result.Status == RequestStatus.Approved)
                {
                    l.CompletedAt = DateTime.UtcNow;
                    var balance = await db.LeaveBalances.FirstOrDefaultAsync(b =>
                        b.EmployeeId == l.EmployeeId && b.LeaveTypeId == l.LeaveTypeId && b.Year == l.StartDate.Year);
                    if (balance is not null) balance.Used += l.Days;
                }
                await notifications.NotifyEmployeeAsync(l.EmployeeId, NotificationType.ApprovalResult,
                    $"Leave request {StatusLabel(result.Status)}",
                    $"Your {l.LeaveType?.Name} request ({l.StartDate:MMM d} - {l.EndDate:MMM d}) is {StatusLabel(result.Status)}.");
                break;
            }
            case RequestType.Overtime:
            {
                var o = await db.OvertimeRequests.FirstAsync(x => x.Id == req.RequestId);
                o.Status = result.Status;
                o.CurrentApprovalLevel = result.NextLevel;
                if (result.Status == RequestStatus.Approved) o.CompletedAt = DateTime.UtcNow;
                await notifications.NotifyEmployeeAsync(o.EmployeeId, NotificationType.ApprovalResult,
                    $"Overtime request {StatusLabel(result.Status)}",
                    $"Your overtime request for {o.OvertimeDate:MMM d} is {StatusLabel(result.Status)}.");
                break;
            }
            case RequestType.CashAdvance:
            case RequestType.Loan:
            {
                var loan = await db.Loans.FirstAsync(x => x.Id == req.RequestId);
                loan.ApprovalStatus = result.Status;
                loan.CurrentApprovalLevel = result.NextLevel;
                if (result.Status == RequestStatus.Approved) { loan.Status = LoanStatus.Active; loan.Balance = loan.Principal; }
                else if (result.Status == RequestStatus.Rejected) loan.Status = LoanStatus.Rejected;
                await notifications.NotifyEmployeeAsync(loan.EmployeeId, NotificationType.ApprovalResult,
                    $"{(loan.Type == LoanType.CashAdvance ? "Cash advance" : "Loan")} {StatusLabel(result.Status)}",
                    $"Your application for ₱{loan.Principal:n2} is {StatusLabel(result.Status)}.");
                break;
            }
            case RequestType.AttendanceCorrection:
                return BadRequest(new { message = "Use the attendance corrections endpoint for corrections." });
        }

        await db.SaveChangesAsync();
        return Ok(new { status = result.Status.ToString(), completed = result.Completed });
    }

    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = db.ApprovalActions.Where(a => a.ActedByUserId != null && !a.HiddenFromHistory);
        if (User.Role() != UserRole.SuperAdministrator && !User.IsHr())
            q = q.Where(a => a.ActedByUserId == User.UserId());
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(a => a.ActedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new
            {
                a.Id, requestType = a.RequestType.ToString(), a.RequestId, a.Level, a.StepName,
                status = a.Status.ToString(), a.ActedByUserId, a.ActedByName, a.Remarks, a.ActedAt
            }).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpDelete("history/{id:int}")]
    public async Task<IActionResult> DeleteHistory(int id)
    {
        var action = await db.ApprovalActions.FirstOrDefaultAsync(a => a.Id == id && a.ActedByUserId != null);
        if (action is null) return NotFound(new { message = "Approval history entry not found." });
        if (action.HiddenFromHistory) return Ok(new { message = "Entry already removed from history." });

        var role = User.Role();
        var userId = User.UserId();
        if (role != UserRole.SuperAdministrator && action.ActedByUserId != userId)
            return StatusCode(403, new { message = "You can only remove approval history entries that you acted on." });

        action.HiddenFromHistory = true;
        audit.Log(AuditCategory.Approval, $"Removed approval history entry #{id}",
            action.RequestType.ToString(), action.RequestId.ToString(),
            $"{action.StepName} · {action.Status}");
        await db.SaveChangesAsync();
        return Ok(new { message = "Approval history entry removed." });
    }

    /// <summary>Approval chain for a given request (for detail views).</summary>
    [HttpGet("chain")]
    public async Task<IActionResult> Chain([FromQuery] string requestType, [FromQuery] int requestId)
    {
        if (!Enum.TryParse<RequestType>(requestType, out var type)) return BadRequest();
        var templates = await db.WorkflowTemplateSteps.Where(t => t.RequestType == type).ToListAsync();
        var chain = await db.ApprovalActions
            .Where(a => a.RequestType == type && a.RequestId == requestId)
            .OrderBy(a => a.Level)
            .Select(a => new { a.Level, a.StepName, status = a.Status.ToString(), a.ActedByName, a.Remarks, a.ActedAt })
            .ToListAsync();
        return Ok(chain.Select(c => new
        {
            c.Level, c.StepName, c.status, c.ActedByName, c.Remarks, c.ActedAt,
            isApplyOnly = templates.FirstOrDefault(t => t.Level == c.Level)?.IsApplyOnlyStep ?? false
        }));
    }

    private static string StatusLabel(RequestStatus s) => s switch
    {
        RequestStatus.Approved => "approved",
        RequestStatus.Rejected => "rejected",
        RequestStatus.ReturnedForRevision => "returned for revision",
        RequestStatus.InProgress => "endorsed to the next approver",
        _ => s.ToString().ToLower()
    };
}
