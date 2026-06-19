using Hris.Api.Data;
using Hris.Api.Services;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Controllers;

[ApiController]
[Route("api/overtime")]
[Authorize]
public class OvertimeController(HrisDbContext db, ApprovalService approvals, NotificationService notifications) : ControllerBase
{
    [HttpGet("requests")]
    public async Task<IActionResult> Requests([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var q = db.OvertimeRequests.Include(o => o.Employee).ThenInclude(e => e!.Department).AsQueryable();
        if (User.Role() == UserRole.Employee) q = q.Where(o => o.EmployeeId == User.EmployeeId());
        else if (User.Role() == UserRole.DepartmentHead)
        {
            var me = await db.Users.Include(u => u.Employee).FirstAsync(u => u.Id == User.UserId());
            q = q.Where(o => o.Employee!.DepartmentId == me.Employee!.DepartmentId);
        }
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<RequestStatus>(status, out var st)) q = q.Where(o => o.Status == st);

        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new
            {
                o.Id, o.OvertimeDate, o.StartTime, o.EndTime, o.Hours, o.Reason,
                status = o.Status.ToString(), o.CurrentApprovalLevel, o.ComputedPay, o.CreatedAt,
                employee = new { o.Employee!.Id, o.Employee.EmployeeCode, name = o.Employee.FirstName + " " + o.Employee.LastName, department = o.Employee.Department!.Name }
            }).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("requests/{id:int}")]
    public async Task<IActionResult> GetRequest(int id)
    {
        var o = await db.OvertimeRequests.Include(x => x.Employee).FirstOrDefaultAsync(x => x.Id == id);
        if (o is null) return NotFound();
        var chain = await db.ApprovalActions
            .Where(a => a.RequestType == RequestType.Overtime && a.RequestId == id)
            .OrderBy(a => a.Level).ToListAsync();
        return Ok(new { request = o, approvalChain = chain });
    }

    [HttpPost("requests")]
    public async Task<IActionResult> Submit(OvertimeRequest input)
    {
        var eid = User.EmployeeId();
        if (User.Role() == UserRole.Employee)
        {
            if (eid is null) return BadRequest(new { message = "No employee profile linked." });
            input.EmployeeId = eid.Value;
        }
        else if (input.EmployeeId <= 0 && eid is int linked)
            input.EmployeeId = linked;

        if (input.EmployeeId <= 0) return BadRequest(new { message = "Employee is required." });

        if (input.Hours <= 0)
        {
            var span = input.EndTime - input.StartTime;
            input.Hours = Math.Round((decimal)span.TotalHours, 2);
            if (input.Hours <= 0) input.Hours += 24; // overnight OT
        }

        // OT policy: only recognized once the employee exceeds 30 minutes beyond regular hours.
        if (input.Hours <= 0.5m)
            return BadRequest(new { message = "Overtime is only recognized when it exceeds 30 minutes beyond regular working hours." });

        var emp = await db.Employees.FindAsync(input.EmployeeId);
        if (emp is not null)
        {
            // HR policy: Basic Salary / 24 days / 8 hours = hourly rate.
            var hourly = (emp.DailyRate ?? Math.Round(emp.MonthlySalary / 24m, 2)) / 8;
            input.ComputedPay = Math.Round(input.Hours * hourly * 1.25m, 2);
        }

        input.Id = 0;
        input.Status = RequestStatus.Pending;
        input.CurrentApprovalLevel = 1;
        input.Employee = null;
        db.OvertimeRequests.Add(input);
        await db.SaveChangesAsync();
        await approvals.InitializeChainAsync(RequestType.Overtime, input.Id, input.EmployeeId,
            $"Overtime on {input.OvertimeDate:MMM d} ({input.Hours} hour/s)", User.Role());
        await db.SaveChangesAsync();
        return Ok(input);
    }

    [HttpPost("requests/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var o = await db.OvertimeRequests.FindAsync(id);
        if (o is null) return NotFound();
        if (User.Role() == UserRole.Employee && o.EmployeeId != User.EmployeeId()) return Forbid();
        if (o.Status is RequestStatus.Approved or RequestStatus.Rejected)
            return BadRequest(new { message = "Completed requests cannot be cancelled." });
        o.Status = RequestStatus.Cancelled;
        await db.SaveChangesAsync();
        return Ok(new { status = o.Status.ToString() });
    }

    // ---- OT Corrections (missed / incorrect encoding) ----
    [HttpGet("corrections")]
    public async Task<IActionResult> Corrections([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var q = db.OvertimeCorrections.Include(o => o.Employee).ThenInclude(e => e!.Department).AsQueryable();
        if (User.Role() == UserRole.Employee) q = q.Where(o => o.EmployeeId == User.EmployeeId());
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<RequestStatus>(status, out var st)) q = q.Where(o => o.Status == st);

        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new
            {
                o.Id, o.OvertimeDate, o.StartTime, o.EndTime, o.Hours,
                issueType = o.IssueType.ToString(), o.Reason, o.SupportingDocument,
                status = o.Status.ToString(), o.CurrentApprovalLevel, o.PayrollAppliedAt, o.CreatedAt,
                employee = new { o.Employee!.Id, o.Employee.EmployeeCode, name = o.Employee.FirstName + " " + o.Employee.LastName, department = o.Employee.Department!.Name }
            }).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpPost("corrections")]
    public async Task<IActionResult> SubmitCorrection(OvertimeCorrection input)
    {
        var eid = User.EmployeeId();
        if (User.Role() == UserRole.Employee)
        {
            if (eid is null) return BadRequest(new { message = "No employee profile linked." });
            input.EmployeeId = eid.Value;
        }
        if (input.EmployeeId <= 0) return BadRequest(new { message = "Employee is required." });
        if (string.IsNullOrWhiteSpace(input.Reason)) return BadRequest(new { message = "Reason is required." });

        if (input.Hours <= 0)
        {
            var span = input.EndTime - input.StartTime;
            input.Hours = Math.Round((decimal)span.TotalHours, 2);
            if (input.Hours <= 0) input.Hours += 24;
        }
        if (input.Hours <= 0.5m)
            return BadRequest(new { message = "Overtime correction must exceed 30 minutes." });

        input.Id = 0;
        input.Status = RequestStatus.Pending;
        input.CurrentApprovalLevel = 1;
        input.RequestedByUserId = User.UserId();
        input.Employee = null;
        db.OvertimeCorrections.Add(input);
        await db.SaveChangesAsync();
        await approvals.InitializeChainAsync(RequestType.OvertimeCorrection, input.Id, input.EmployeeId,
            $"{input.IssueType}: {input.OvertimeDate:MMM d} ({input.Hours} hr/s)");
        await db.SaveChangesAsync();
        return Ok(input);
    }

    [HttpPost("corrections/{id:int}/act")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)},{nameof(UserRole.DepartmentHead)},{nameof(UserRole.PayrollOfficer)}")]
    public async Task<IActionResult> ActOnCorrection(int id, [FromBody] ActRequest req)
    {
        var c = await db.OvertimeCorrections.FindAsync(id);
        if (c is null) return NotFound();

        var pendingStep = await db.ApprovalActions
            .FirstOrDefaultAsync(a => a.RequestType == RequestType.OvertimeCorrection
                && a.RequestId == id && a.Status == ApprovalStepStatus.Pending);
        if (pendingStep is null) return BadRequest(new { message = "No pending approval step." });

        var template = await db.WorkflowTemplateSteps
            .FirstOrDefaultAsync(s => s.RequestType == RequestType.OvertimeCorrection && s.Level == pendingStep.Level);
        if (template?.IsApplyOnlyStep == true && !req.Approve)
            return BadRequest(new { message = "Payroll can only apply approved OT corrections." });

        var result = await approvals.ActAsync(RequestType.OvertimeCorrection, id, User.UserId(), User.DisplayName(), User.Role(), req.Approve, req.Remarks, req.Return);
        c.Status = result.Status;
        c.CurrentApprovalLevel = result.NextLevel;
        c.ApproverRemarks = req.Remarks;

        if (result.Completed && result.Status == RequestStatus.Approved && template?.IsApplyOnlyStep == true)
        {
            var ot = await CorrectionWorkflowHelper.CreateApprovedOvertimeFromCorrectionAsync(db, c);
            c.CreatedOvertimeRequestId = ot.Id;
            c.PayrollAppliedAt = DateTime.UtcNow;
        }

        await notifications.NotifyEmployeeAsync(c.EmployeeId, NotificationType.ApprovalResult,
            $"Overtime correction {result.Status}",
            $"Your OT correction for {c.OvertimeDate:MMM d} is now {result.Status}.");
        await db.SaveChangesAsync();
        return Ok(new { status = c.Status.ToString(), overtimeRequestId = c.CreatedOvertimeRequestId });
    }

    public record ActRequest(bool Approve, string? Remarks, bool Return = false);
}
