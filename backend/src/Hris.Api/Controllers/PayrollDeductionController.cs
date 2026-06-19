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
public class PayrollDeductionController(HrisDbContext db, PayrollDeductionService deductions, AuditService audit) : ControllerBase
{
    private const string PayrollRoles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.PayrollOfficer)}";

    [HttpGet("deduction-types")]
    [Authorize(Roles = PayrollRoles)]
    public async Task<IActionResult> DeductionTypes([FromQuery] int? cutoffId)
    {
        var q = db.DeductionTypes.Where(t => t.IsActive).OrderBy(t => t.SortOrder);
        var types = await q.ToListAsync();

        if (cutoffId is int cid)
        {
            var cutoff = await db.PayrollCutoffs.FindAsync(cid);
            if (cutoff is null) return NotFound();
            var isFirstHalf = cutoff.PeriodStart.Day <= 15;
            types = types.Where(t => PayrollDeductionService.TypeAppliesToCutoff(t, isFirstHalf)).ToList();
        }

        return Ok(types.Select(t => new
        {
            t.Id, t.Code, t.Name,
            applicableHalf = t.ApplicableHalf.ToString(),
            t.SortOrder
        }));
    }

    [HttpGet("deduction-templates")]
    [Authorize(Roles = PayrollRoles)]
    public async Task<IActionResult> Templates()
    {
        var templates = await db.DeductionTemplates
            .Include(t => t.Items).ThenInclude(i => i.DeductionType)
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync();

        return Ok(templates.Select(t => new
        {
            t.Id, t.Name, t.Description,
            items = t.Items.Select(i => new { i.DeductionTypeId, code = i.DeductionType!.Code, name = i.DeductionType.Name })
        }));
    }

    [HttpGet("cutoffs/{cutoffId:int}/deduction-selections")]
    [Authorize(Roles = PayrollRoles)]
    public async Task<IActionResult> CutoffSelections(int cutoffId, [FromQuery] int? employeeId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var cutoff = await db.PayrollCutoffs.FindAsync(cutoffId);
        if (cutoff is null) return NotFound();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = db.PayrollCutoffDeductionSelections
            .Include(s => s.Employee)
            .Include(s => s.EmployeeDeduction).ThenInclude(d => d!.DeductionType)
            .Where(s => s.PayrollCutoffId == cutoffId);

        if (employeeId.HasValue) q = q.Where(s => s.EmployeeId == employeeId);

        var total = await q.CountAsync();
        var rows = await q.OrderBy(s => s.Employee!.EmployeeCode).ThenBy(s => s.EmployeeDeduction!.DeductionType!.SortOrder)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        var isFirstHalf = cutoff.PeriodStart.Day <= 15;
        return Ok(new
        {
            total, page, pageSize,
            cutoffHalf = isFirstHalf ? "1-15" : "16-30",
            cutoffFlags = new
            {
                cutoff.DeductSss,
                cutoff.DeductPhilHealth,
                deductPagIbig = cutoff.DeductPagIbig,
                cutoff.DeductTax,
                cutoff.DeductLoans,
                cutoff.DeductOtherDeductions
            },
            items = rows.Select(s => new
            {
                s.Id, s.EmployeeId, s.EmployeeDeductionId, s.IsApplied,
                employee = new { s.Employee!.Id, s.Employee.EmployeeCode, name = s.Employee.FirstName + " " + s.Employee.LastName },
                deduction = new
                {
                    typeCode = s.EmployeeDeduction!.DeductionType!.Code,
                    typeName = s.EmployeeDeduction.DeductionType.Name,
                    s.EmployeeDeduction.Amount,
                    s.EmployeeDeduction.RemainingBalance,
                    frequency = s.EmployeeDeduction.Frequency.ToString()
                }
            })
        });
    }

    [HttpPost("cutoffs/{cutoffId:int}/deduction-selections/init")]
    [Authorize(Roles = PayrollRoles)]
    public async Task<IActionResult> InitCutoffSelections(int cutoffId)
    {
        var cutoff = await db.PayrollCutoffs.FindAsync(cutoffId);
        if (cutoff is null) return NotFound();
        if (cutoff.Status is PayrollStatus.Approved or PayrollStatus.Released or PayrollStatus.Closed)
            return BadRequest(new { message = "Cannot modify deductions for a finalized cutoff." });

        await deductions.InitializeCutoffSelectionsAsync(cutoffId);
        audit.Log(AuditCategory.Payroll, $"Initialized deduction checklist for cutoff #{cutoffId}", nameof(PayrollCutoff), cutoffId.ToString());
        return Ok(new { message = "Deduction checklist initialized." });
    }

    public record SelectionUpdate(int EmployeeDeductionId, bool IsApplied);

    [HttpPut("cutoffs/{cutoffId:int}/deduction-selections")]
    [Authorize(Roles = PayrollRoles)]
    public async Task<IActionResult> SaveCutoffSelections(int cutoffId, [FromBody] List<SelectionUpdate> updates)
    {
        var cutoff = await db.PayrollCutoffs.FindAsync(cutoffId);
        if (cutoff is null) return NotFound();
        if (cutoff.Status is PayrollStatus.Approved or PayrollStatus.Released or PayrollStatus.Closed)
            return BadRequest(new { message = "Cannot modify deductions for a finalized cutoff." });

        if (updates.Count == 0) return Ok(new { updated = 0 });

        var ids = updates.Select(u => u.EmployeeDeductionId).ToHashSet();
        var selections = await db.PayrollCutoffDeductionSelections
            .Where(s => s.PayrollCutoffId == cutoffId && ids.Contains(s.EmployeeDeductionId))
            .ToListAsync();

        var map = updates.ToDictionary(u => u.EmployeeDeductionId);
        foreach (var s in selections)
        {
            if (map.TryGetValue(s.EmployeeDeductionId, out var u))
                s.IsApplied = u.IsApplied;
        }

        await db.SaveChangesAsync();
        audit.Log(AuditCategory.Payroll, $"Updated {selections.Count} deduction selection(s) for cutoff #{cutoffId}", nameof(PayrollCutoff), cutoffId.ToString());
        return Ok(new { updated = selections.Count });
    }
}
