using Hris.Api.Data;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Services;

public class ApprovalService(HrisDbContext db, NotificationService notifications, AuditService audit)
{
    /// <summary>Creates the approval chain for a new request from the workflow template.</summary>
    public async Task InitializeChainAsync(RequestType type, int requestId, int requestorEmployeeId, string requestSummary, UserRole? submitterRole = null)
    {
        var template = await ResolveWorkflowStepsAsync(type, requestorEmployeeId, submitterRole);

        var requestor = await db.Employees.FindAsync(requestorEmployeeId);

        foreach (var step in template)
        {
            db.ApprovalActions.Add(new ApprovalAction
            {
                RequestType = type,
                RequestId = requestId,
                Level = step.Level,
                ApproverRole = step.ApproverRole,
                StepName = step.StepName,
                Status = step.Level == 1 ? ApprovalStepStatus.Pending : ApprovalStepStatus.Waiting
            });
        }

        if (template.Count > 0)
        {
            var first = template[0];
            await notifications.NotifyRoleAsync(first.ApproverRole, NotificationType.ApprovalRequest,
                $"New {TypeName(type)} request",
                $"{requestor?.FullName ?? "An employee"} submitted: {requestSummary}",
                first.ApproverRole == UserRole.DepartmentHead ? requestor?.DepartmentId : null);
        }
        audit.Log(AuditCategory.Approval, $"Submitted {TypeName(type)} request #{requestId}", type.ToString(), requestId.ToString(), requestSummary);
    }

    /// <summary>
    /// Dept heads and HR officers cannot use the standard chain (they are approvers in it).
    /// Their leave / OT / loan / cash-advance requests go: HR Admin → VP → CEO.
    /// </summary>
    private async Task<List<WorkflowTemplateStep>> ResolveWorkflowStepsAsync(RequestType type, int requestorEmployeeId, UserRole? submitterRole)
    {
        if (UsesManagerEscalation(type) && await ShouldUseEscalatedChainAsync(requestorEmployeeId, submitterRole))
            return BuildEscalatedSteps(type);

        return await db.WorkflowTemplateSteps
            .Where(s => s.RequestType == type)
            .OrderBy(s => s.Level)
            .ToListAsync();
    }

    private static bool UsesManagerEscalation(RequestType type) =>
        type is RequestType.Leave or RequestType.Overtime or RequestType.CashAdvance or RequestType.Loan;

    private async Task<bool> ShouldUseEscalatedChainAsync(int employeeId, UserRole? submitterRole)
    {
        if (submitterRole is UserRole.DepartmentHead or UserRole.HrOfficer)
            return true;

        // One employee profile can have multiple user accounts (e.g. hradmin + hrofficer).
        var linkedRoles = await db.Users.AsNoTracking()
            .Where(u => u.EmployeeId == employeeId)
            .Select(u => u.Role)
            .ToListAsync();
        if (linkedRoles.Any(r => r is UserRole.DepartmentHead or UserRole.HrOfficer))
            return true;

        return await db.Departments.AsNoTracking().AnyAsync(d => d.HeadEmployeeId == employeeId);
    }

    private static List<WorkflowTemplateStep> BuildEscalatedSteps(RequestType type) =>
    [
        new() { RequestType = type, Level = 1, ApproverRole = UserRole.HrAdministrator, StepName = "HR Administrator" },
        new() { RequestType = type, Level = 2, ApproverRole = UserRole.VicePresidentHrHead, StepName = "Vice President & HR Head" },
        new() { RequestType = type, Level = 3, ApproverRole = UserRole.PresidentCeo, StepName = "President & CEO" },
    ];

    public record ApprovalResult(bool Completed, RequestStatus Status, int NextLevel);

    /// <summary>Acts on the current pending step. Returns final state of the chain.</summary>
    public async Task<ApprovalResult> ActAsync(RequestType type, int requestId, int actorUserId, string actorName, UserRole actorRole, bool approve, string? remarks, bool returnForRevision = false)
    {
        var chain = await db.ApprovalActions
            .Where(a => a.RequestType == type && a.RequestId == requestId)
            .OrderBy(a => a.Level)
            .ToListAsync();

        var current = chain.FirstOrDefault(a => a.Status == ApprovalStepStatus.Pending)
            ?? throw new InvalidOperationException("No pending approval step for this request.");

        var allowed = actorRole == current.ApproverRole
                      || actorRole is UserRole.SuperAdministrator
                      || (actorRole == UserRole.VicePresidentHrHead && current.ApproverRole is UserRole.DepartmentHead or UserRole.HrOfficer);
        if (!allowed) throw new UnauthorizedAccessException($"This step must be acted on by: {current.StepName}.");

        var templateStep = await db.WorkflowTemplateSteps
            .FirstOrDefaultAsync(s => s.RequestType == type && s.Level == current.Level);
        if (templateStep?.IsApplyOnlyStep == true && !approve)
            throw new InvalidOperationException("Payroll can only acknowledge application to payroll — rejection is not allowed at this step.");

        current.ActedByUserId = actorUserId;
        current.ActedByName = actorName;
        current.Remarks = remarks;
        current.ActedAt = DateTime.UtcNow;

        if (returnForRevision)
        {
            current.Status = ApprovalStepStatus.Returned;
            foreach (var s in chain.Where(s => s.Level > current.Level)) s.Status = ApprovalStepStatus.Skipped;
            audit.Log(AuditCategory.Approval, $"Returned {TypeName(type)} request #{requestId} for revision", type.ToString(), requestId.ToString(), remarks);
            return new ApprovalResult(true, RequestStatus.ReturnedForRevision, current.Level);
        }

        if (!approve)
        {
            current.Status = ApprovalStepStatus.Rejected;
            foreach (var s in chain.Where(s => s.Level > current.Level)) s.Status = ApprovalStepStatus.Skipped;
            audit.Log(AuditCategory.Approval, $"Rejected {TypeName(type)} request #{requestId}", type.ToString(), requestId.ToString(), remarks);
            return new ApprovalResult(true, RequestStatus.Rejected, current.Level);
        }

        current.Status = ApprovalStepStatus.Approved;
        audit.Log(AuditCategory.Approval, $"Approved {TypeName(type)} request #{requestId} (level {current.Level})", type.ToString(), requestId.ToString(), remarks);

        var next = chain.FirstOrDefault(a => a.Level > current.Level && a.Status == ApprovalStepStatus.Waiting);
        if (next is null)
            return new ApprovalResult(true, RequestStatus.Approved, current.Level);

        next.Status = ApprovalStepStatus.Pending;
        await notifications.NotifyRoleAsync(next.ApproverRole, NotificationType.ApprovalRequest,
            $"{TypeName(type)} request awaiting your approval",
            $"Request #{requestId} was endorsed by {current.StepName} and now requires your action.");
        return new ApprovalResult(false, RequestStatus.InProgress, next.Level);
    }

    public static string TypeName(RequestType t) => t switch
    {
        RequestType.Leave => "Leave",
        RequestType.ServiceIncentiveLeave => "SIL",
        RequestType.Overtime => "Overtime",
        RequestType.CashAdvance => "Cash Advance",
        RequestType.Loan => "Loan",
        RequestType.AttendanceCorrection => "Attendance Correction",
        RequestType.OvertimeCorrection => "Overtime Correction",
        RequestType.Payroll => "Payroll",
        _ => t.ToString()
    };
}
