using Hris.Api.Data;

using Hris.Api.Services;

using Hris.Domain;

using Hris.Domain.Entities;

using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Mvc;

using Microsoft.EntityFrameworkCore;



namespace Hris.Api.Controllers;



[ApiController]

[Route("api/payroll")]

[Authorize]

public class PayrollController(HrisDbContext db, PayrollService payroll, AuditService audit, NotificationService notifications) : ControllerBase

{

    private const string PayrollRoles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.PayrollOfficer)}";

    private const string ExecutiveViewRoles = $"{PayrollRoles},{nameof(UserRole.VicePresidentHrHead)},{nameof(UserRole.PresidentCeo)}";

    private const string ExecutiveApproveRoles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.VicePresidentHrHead)},{nameof(UserRole.PresidentCeo)}";



    private const string AdminRoles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)}";

    [HttpGet("cutoffs")]

    [Authorize(Roles = ExecutiveViewRoles)]

    public async Task<IActionResult> Cutoffs([FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = db.PayrollCutoffs.AsQueryable();
        var total = await q.CountAsync();
        var rows = await q.Include(c => c.Payslips)
            .OrderByDescending(c => c.PeriodStart)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        var items = rows.Select(c =>
        {
            var avail = PayrollCutoffPolicy.GetAvailability(c);
            return new
            {
                c.Id, c.Name, c.PeriodStart, c.PeriodEnd, c.PayDate, status = c.Status.ToString(),
                c.ProcessedAt, c.VpApprovedAt, c.VpApprovedByName, c.ApprovedAt, c.ApprovedByName, c.ProcessingError,
                payslipCount = c.Payslips.Count, totalNet = c.Payslips.Sum(p => (decimal?)p.NetPay) ?? 0,
                totalGross = c.Payslips.Sum(p => (decimal?)p.GrossPay) ?? 0,
                canProcess = avail.CanProcess,
                canRelease = avail.CanRelease,
                canReset = avail.CanReset,
                releaseAvailableOn = avail.ReleaseAvailableOn,
                blockReason = avail.BlockReason
            };
        }).ToList();

        return Ok(new { total, page, pageSize, items });
    }



    [HttpPost("cutoffs")]

    [Authorize(Roles = PayrollRoles)]

    public async Task<IActionResult> CreateCutoff(PayrollCutoff c)

    {

        if (string.IsNullOrWhiteSpace(c.Name))

            c.Name = $"{c.PeriodStart:MMM d} - {c.PeriodEnd:MMM d, yyyy}";

        try

        {

            var existing = await db.PayrollCutoffs.AsNoTracking().ToListAsync();

            PayrollCutoffPolicy.ValidateCreate(c, existing);

        }

        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        db.PayrollCutoffs.Add(c);

        audit.Log(AuditCategory.Payroll, $"Created payroll cutoff '{c.Name}'", nameof(PayrollCutoff));

        await db.SaveChangesAsync();

        return Ok(c);

    }



    [HttpPost("cutoffs/{id:int}/process")]

    [Authorize(Roles = PayrollRoles)]

    public async Task<IActionResult> Process(int id)

    {

        var c = await db.PayrollCutoffs.FindAsync(id);

        if (c is null) return NotFound();

        if (c.Status == PayrollStatus.Processing)

            return Conflict(new { message = "Payroll is already being processed in the background." });

        if (c.Status is PayrollStatus.ForApproval or PayrollStatus.ForCeoApproval or PayrollStatus.Approved or PayrollStatus.Released or PayrollStatus.Closed)

            return BadRequest(new { message = "Cutoff cannot be reprocessed in its current state." });

        try { PayrollCutoffPolicy.ValidateProcess(c); }

        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        c.Status = PayrollStatus.Processing;

        c.ProcessingError = null;

        c.VpApprovedAt = null;

        c.VpApprovedByName = null;

        c.ApprovedAt = null;

        c.ApprovedByName = null;

        audit.Log(AuditCategory.Payroll, $"Queued payroll cutoff '{c.Name}' for background processing", nameof(PayrollCutoff), id.ToString());

        await db.SaveChangesAsync();

        return Accepted(new { status = "Processing", cutoffId = id, message = "Payroll queued. Refresh in a few seconds to see results." });

    }



    [HttpPost("cutoffs/{id:int}/approve")]

    [Authorize(Roles = ExecutiveApproveRoles)]

    public async Task<IActionResult> Approve(int id, [FromBody] ApproveRequest req)

    {

        var c = await db.PayrollCutoffs.FindAsync(id);

        if (c is null) return NotFound();

        var role = User.Role();



        if (!req.Approve)

        {

            if (c.Status is not (PayrollStatus.ForApproval or PayrollStatus.ForCeoApproval))

                return BadRequest(new { message = "Cutoff is not awaiting executive approval." });

            c.Status = PayrollStatus.Draft;

            c.VpApprovedAt = null;

            c.VpApprovedByName = null;

            c.ApprovedAt = null;

            c.ApprovedByName = null;

            audit.Log(AuditCategory.Payroll, $"Rejected payroll cutoff '{c.Name}'", nameof(PayrollCutoff), id.ToString(), req.Remarks);

            await db.SaveChangesAsync();

            return Ok(new { status = c.Status.ToString() });

        }



        if (c.Status == PayrollStatus.ForApproval)

        {

            if (role is not (UserRole.VicePresidentHrHead or UserRole.SuperAdministrator))

                return Forbid();

            c.Status = PayrollStatus.ForCeoApproval;

            c.VpApprovedAt = DateTime.UtcNow;

            c.VpApprovedByName = User.DisplayName();

            audit.Log(AuditCategory.Payroll, $"VP approved payroll cutoff '{c.Name}' — forwarded to CEO", nameof(PayrollCutoff), id.ToString());

            await notifications.NotifyRoleAsync(UserRole.PresidentCeo, NotificationType.Payroll,

                "Payroll awaiting CEO approval", $"Payroll cutoff '{c.Name}' has been approved by {c.VpApprovedByName} and requires your final approval.");

            await db.SaveChangesAsync();

            return Ok(new { status = c.Status.ToString(), message = "Forwarded to President & CEO for final approval." });

        }



        if (c.Status == PayrollStatus.ForCeoApproval)

        {

            if (role is not (UserRole.PresidentCeo or UserRole.SuperAdministrator))

                return Forbid();

            c.Status = PayrollStatus.Approved;

            c.ApprovedAt = DateTime.UtcNow;

            c.ApprovedByName = User.DisplayName();

            audit.Log(AuditCategory.Payroll, $"CEO approved payroll cutoff '{c.Name}'", nameof(PayrollCutoff), id.ToString());

            await notifications.NotifyRoleAsync(UserRole.PayrollOfficer, NotificationType.Payroll,

                "Payroll approved", $"Payroll cutoff '{c.Name}' is fully approved and ready for release.");

            await db.SaveChangesAsync();

            return Ok(new { status = c.Status.ToString(), message = "Payroll fully approved." });

        }



        return BadRequest(new { message = "Cutoff is not awaiting your approval step." });

    }

    public record ApproveRequest(bool Approve, string? Remarks);



    [HttpPost("cutoffs/{id:int}/release")]

    [Authorize(Roles = PayrollRoles)]

    public async Task<IActionResult> Release(int id)

    {

        try

        {

            await payroll.ReleaseCutoffAsync(id);

            var employeeIds = await db.Payslips.Where(p => p.PayrollCutoffId == id).Select(p => p.EmployeeId).ToListAsync();

            foreach (var eid in employeeIds)

                await notifications.NotifyEmployeeAsync(eid, NotificationType.Payroll, "Payslip available", "Your payslip for the latest cutoff is now available in the self-service portal.");

            await db.SaveChangesAsync();

            return Ok(new { status = "Released" });

        }

        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

    }



    [HttpPost("cutoffs/{id:int}/reset")]

    [Authorize(Roles = PayrollRoles)]

    public async Task<IActionResult> Reset(int id)

    {

        var c = await db.PayrollCutoffs.Include(x => x.Payslips).FirstOrDefaultAsync(x => x.Id == id);

        if (c is null) return NotFound();

        if (c.Status is PayrollStatus.Released or PayrollStatus.Closed)

            return BadRequest(new { message = "Released cutoffs cannot be reset." });

        var avail = PayrollCutoffPolicy.GetAvailability(c);

        if (!avail.CanReset)

            return BadRequest(new { message = "This cutoff is not eligible for reset." });

        db.Payslips.RemoveRange(c.Payslips);
        var sel = await db.PayrollCutoffDeductionSelections.Where(s => s.PayrollCutoffId == id).ToListAsync();
        db.PayrollCutoffDeductionSelections.RemoveRange(sel);

        c.Status = PayrollStatus.Draft;

        c.ProcessedAt = null;

        c.VpApprovedAt = null;

        c.VpApprovedByName = null;

        c.ApprovedAt = null;

        c.ApprovedByName = null;

        c.ProcessingError = null;

        audit.Log(AuditCategory.Payroll, $"Reset premature payroll cutoff '{c.Name}' to draft", nameof(PayrollCutoff), id.ToString());

        await db.SaveChangesAsync();

        return Ok(new { message = "Cutoff reset to draft. Re-process after the period ends and the release window opens.", status = c.Status.ToString() });

    }



    [HttpDelete("cutoffs/{id:int}")]

    [Authorize(Roles = AdminRoles)]

    public async Task<IActionResult> DeleteCutoff(int id)

    {

        var c = await db.PayrollCutoffs.Include(x => x.Payslips).FirstOrDefaultAsync(x => x.Id == id);

        if (c is null) return NotFound(new { message = "Payroll cutoff not found." });

        if (c.Status is PayrollStatus.Released or PayrollStatus.Closed)

            return BadRequest(new { message = "Released or closed cutoffs cannot be deleted. They are kept for audit and payslip history." });

        var payslipIds = c.Payslips.Select(p => p.Id).ToList();

        if (payslipIds.Count > 0)

        {

            var loanPayments = await db.LoanPayments.Where(p => p.PayslipId != null && payslipIds.Contains(p.PayslipId.Value)).ToListAsync();

            db.LoanPayments.RemoveRange(loanPayments);

            db.Payslips.RemoveRange(c.Payslips);

        }

        db.PayrollCutoffs.Remove(c);

        audit.Log(AuditCategory.Payroll, $"Deleted payroll cutoff '{c.Name}'", nameof(PayrollCutoff), id.ToString());

        await db.SaveChangesAsync();

        return Ok(new { message = $"Payroll cutoff \"{c.Name}\" deleted permanently." });

    }



    [HttpGet("cutoffs/{id:int}/payslips")]

    [Authorize(Roles = ExecutiveViewRoles)]

    public async Task<IActionResult> Payslips(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)

    {

        var execIds = await ExecutiveExemption.GetExemptEmployeeIdsAsync(db);

        var q = db.Payslips.Include(p => p.Employee).ThenInclude(e => e!.Department)

            .Where(p => p.PayrollCutoffId == id && !execIds.Contains(p.EmployeeId));



        var total = await q.CountAsync();

        var items = await q.OrderBy(p => p.Employee!.EmployeeCode)

            .Skip((page - 1) * pageSize).Take(pageSize)

            .Select(p => new

            {

                p.Id, p.BasicPay, p.OvertimePay, p.Allowances, p.Bonuses, p.GrossPay,

                p.AbsenceDeduction, p.LateDeduction, p.SssEmployee, p.PhilHealthEmployee, p.PagIbigEmployee,

                p.WithholdingTax, p.LoanDeductions, p.OtherDeductions, p.TotalDeductions, p.NetPay,

                p.DaysWorked, p.DaysAbsent, p.LateMinutes, p.OvertimeHours,

                employee = new { p.Employee!.Id, p.Employee.EmployeeCode, name = p.Employee.FirstName + " " + p.Employee.LastName, department = p.Employee.Department!.Name }

            }).ToListAsync();



        return Ok(new { total, items });

    }



    [HttpGet("payslips/{id:int}")]

    public async Task<IActionResult> Payslip(int id)

    {

        var p = await db.Payslips.Include(x => x.Employee).ThenInclude(e => e!.Position)

            .Include(x => x.Employee).ThenInclude(e => e!.Department)

            .Include(x => x.PayrollCutoff)

            .FirstOrDefaultAsync(x => x.Id == id);

        if (p is null) return NotFound();

        var role = User.Role();

        if (role is UserRole.Employee or UserRole.DepartmentHead or UserRole.Supervisor && p.EmployeeId != User.EmployeeId())

            return Forbid();

        if (role is UserRole.PresidentCeo or UserRole.VicePresidentHrHead)

        {

            var allowed = p.PayrollCutoff!.Status is PayrollStatus.ForApproval or PayrollStatus.ForCeoApproval

                or PayrollStatus.Approved or PayrollStatus.Released or PayrollStatus.Closed;

            if (!allowed) return Forbid();

        }

        return Ok(p);

    }



    /// <summary>Self-service: my released payslips.</summary>

    [HttpGet("my-payslips")]
    public async Task<IActionResult> MyPayslips([FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var eid = User.EmployeeId();
        if (eid is null) return Ok(new { total = 0, page, pageSize, items = Array.Empty<object>() });

        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = db.Payslips.Include(p => p.PayrollCutoff)
            .Where(p => p.EmployeeId == eid && (p.PayrollCutoff!.Status == PayrollStatus.Released || p.PayrollCutoff.Status == PayrollStatus.Closed));

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(p => p.PayrollCutoff!.PeriodStart)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new
            {
                p.Id, cutoff = p.PayrollCutoff!.Name, p.PayrollCutoff.PayDate,
                p.GrossPay, p.TotalDeductions, p.NetPay
            }).ToListAsync();
        return Ok(new { total, page, pageSize, items });

    }



    // ---- Pay components (allowances/deductions) ----

    [HttpGet("components")]

    [Authorize(Roles = PayrollRoles)]

    public async Task<IActionResult> Components([FromQuery] int? employeeId)

    {

        var q = db.PayComponents.Include(c => c.Employee).Where(c => c.IsActive);

        if (employeeId.HasValue) q = q.Where(c => c.EmployeeId == employeeId);

        return Ok(await q.Select(c => new

        {

            c.Id, type = c.Type.ToString(), c.Name, c.Amount, c.PerCutoff, c.IsRecurring, c.EffectiveFrom, c.EffectiveTo,

            employee = new { c.Employee!.Id, c.Employee.EmployeeCode, name = c.Employee.FirstName + " " + c.Employee.LastName }

        }).ToListAsync());

    }



    [HttpPost("components")]

    [Authorize(Roles = PayrollRoles)]

    public async Task<IActionResult> AddComponent(PayComponent c)

    {

        c.Employee = null;

        db.PayComponents.Add(c);

        audit.Log(AuditCategory.Payroll, $"Added pay component '{c.Name}' for employee #{c.EmployeeId}", nameof(PayComponent));

        await db.SaveChangesAsync();

        return Ok(c);

    }



    [HttpDelete("components/{id:int}")]

    [Authorize(Roles = PayrollRoles)]

    public async Task<IActionResult> RemoveComponent(int id)

    {

        var c = await db.PayComponents.FindAsync(id);

        if (c is null) return NotFound();

        c.IsActive = false;

        await db.SaveChangesAsync();

        return Ok();

    }

}


