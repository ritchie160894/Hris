using Hris.Api.Data;
using Hris.Api.Services;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Controllers;

[ApiController]
[Route("api/employees")]
[Authorize]
public class EmployeesController(HrisDbContext db, AuditService audit, IWebHostEnvironment env, PayrollDeductionService deductions) : ControllerBase
{
    private static readonly string[] HrRoles = [nameof(UserRole.SuperAdministrator), nameof(UserRole.HrAdministrator), nameof(UserRole.HrOfficer), nameof(UserRole.VicePresidentHrHead)];
    private static readonly string[] PayrollRoles = [nameof(UserRole.SuperAdministrator), nameof(UserRole.HrAdministrator), nameof(UserRole.PayrollOfficer)];

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] int? departmentId, [FromQuery] int? branchId,
        [FromQuery] string? status, [FromQuery] bool activeOnly = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var q = db.Employees
            .Include(e => e.Department).Include(e => e.Position).Include(e => e.Branch).Include(e => e.Site)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(e => e.FirstName.Contains(search) || e.LastName.Contains(search) || e.EmployeeCode.Contains(search));
        if (departmentId.HasValue) q = q.Where(e => e.DepartmentId == departmentId);
        if (branchId.HasValue) q = q.Where(e => e.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<EmploymentStatus>(status, out var st))
            q = q.Where(e => e.Status == st);
        else if (activeOnly)
            q = q.Where(e => e.Status != EmploymentStatus.Resigned && e.Status != EmploymentStatus.Terminated && e.Status != EmploymentStatus.Retired);

        // Department heads only see their own department
        if (User.Role() == UserRole.DepartmentHead)
        {
            var me = await db.Users.Include(u => u.Employee).FirstAsync(u => u.Id == User.UserId());
            q = q.Where(e => e.DepartmentId == me.Employee!.DepartmentId);
        }

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(e => e.HireDate).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new
            {
                e.Id, e.EmployeeCode, e.FirstName, e.MiddleName, e.LastName, fullName = e.FirstName + " " + e.LastName,
                e.PhotoUrl, e.Email, e.ContactNumber,
                department = e.Department!.Name, position = e.Position!.Title,
                branch = e.Branch!.Name, site = e.Site!.Name,
                status = e.Status.ToString(), e.HireDate, e.MonthlySalary
            })
            .ToListAsync();

        return Ok(new { total, items });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var e = await db.Employees
            .Include(x => x.Department).Include(x => x.Position).Include(x => x.Branch).Include(x => x.Site)
            .Include(x => x.Manager).Include(x => x.EmergencyContacts).Include(x => x.Documents)
            .Include(x => x.BiometricTemplates)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();

        // employees can only view their own full profile unless HR, payroll, or dept head
        if (!User.IsHr() && User.Role() is not (UserRole.DepartmentHead or UserRole.PayrollOfficer) && User.EmployeeId() != id)
            return Forbid();

        var history = await db.EmployeeHistories.Where(h => h.EmployeeId == id).OrderByDescending(h => h.EffectiveDate).ToListAsync();

        return Ok(new { employee = e, history });
    }

    [HttpPost]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)}")]
    public async Task<IActionResult> Create(Employee employee)
    {
        if (await db.Employees.AnyAsync(e => e.EmployeeCode == employee.EmployeeCode))
            return BadRequest(new { message = "Employee code already exists." });
        employee.BiometricUserId ??= employee.EmployeeCode;
        employee.Status = EmploymentStatus.Probationary;
        SyncCompensationRates(employee);
        db.Employees.Add(employee);
        db.EmployeeHistories.Add(new EmployeeHistory
        {
            Employee = employee, EventType = "Hired",
            Description = "Employee record created (Probationary)", EffectiveDate = employee.HireDate,
            ChangedByUserName = User.DisplayName()
        });

        await EnsureEmergencyLeaveBalanceAsync(employee, DateTime.Today.Year);

        audit.Log(AuditCategory.RecordChange, $"Created employee {employee.EmployeeCode}", nameof(Employee), employee.EmployeeCode);
        await db.SaveChangesAsync();
        return Ok(employee);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)}")]
    public async Task<IActionResult> Update(int id, Employee input)
    {
        var e = await db.Employees.FindAsync(id);
        if (e is null) return NotFound();

        if (e.MonthlySalary != input.MonthlySalary)
            db.EmployeeHistories.Add(new EmployeeHistory { EmployeeId = id, EventType = "SalaryChange", Description = $"Salary changed from {e.MonthlySalary:n2} to {input.MonthlySalary:n2}", EffectiveDate = DateOnly.FromDateTime(DateTime.Today), ChangedByUserName = User.DisplayName() });
        if (e.Status != input.Status)
        {
            db.EmployeeHistories.Add(new EmployeeHistory { EmployeeId = id, EventType = "StatusChange", Description = $"Status changed from {e.Status} to {input.Status}", EffectiveDate = DateOnly.FromDateTime(DateTime.Today), ChangedByUserName = User.DisplayName() });
            if (e.Status != EmploymentStatus.Regular && input.Status == EmploymentStatus.Regular)
            {
                input.RegularizationDate ??= DateOnly.FromDateTime(DateTime.Today);
                db.EmployeeHistories.Add(new EmployeeHistory { EmployeeId = id, EventType = "Regularized", Description = "Employee regularized — SIL entitlement granted", EffectiveDate = input.RegularizationDate.Value, ChangedByUserName = User.DisplayName() });
            }
        }
        if (e.DepartmentId != input.DepartmentId)
            db.EmployeeHistories.Add(new EmployeeHistory { EmployeeId = id, EventType = "Transferred", Description = "Department assignment changed", EffectiveDate = DateOnly.FromDateTime(DateTime.Today), ChangedByUserName = User.DisplayName() });

        db.Entry(e).CurrentValues.SetValues(input);
        e.Id = id;
        SyncCompensationRates(e);
        e.UpdatedAt = DateTime.UtcNow;

        if (e.Status == EmploymentStatus.Regular)
            await EnsureSilBalanceAsync(e.Id, DateTime.Today.Year);

        audit.Log(AuditCategory.RecordChange, $"Updated employee {e.EmployeeCode}", nameof(Employee), id.ToString());
        await db.SaveChangesAsync();
        return Ok(e);
    }

    /// <summary>Marks an employee as separated (resigned/terminated), deactivates their login, and keeps payroll/history records.</summary>
    [HttpPost("{id:int}/separate")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)}")]
    public async Task<IActionResult> Separate(int id, [FromBody] SeparateEmployeeRequest req)
    {
        var e = await db.Employees.FindAsync(id);
        if (e is null) return NotFound();

        if (e.Status is EmploymentStatus.Resigned or EmploymentStatus.Terminated or EmploymentStatus.Retired)
            return BadRequest(new { message = "Employee is already separated from the company." });

        if (!Enum.TryParse<EmploymentStatus>(req.Status, out var status)
            || status is not (EmploymentStatus.Resigned or EmploymentStatus.Terminated))
            return BadRequest(new { message = "Separation status must be Resigned or Terminated." });

        var sepDate = req.SeparationDate ?? DateOnly.FromDateTime(DateTime.Today);
        e.Status = status;
        e.SeparationDate = sepDate;
        e.UpdatedAt = DateTime.UtcNow;

        foreach (var user in await db.Users.Where(u => u.EmployeeId == id).ToListAsync())
            user.IsActive = false;

        foreach (var dept in await db.Departments.Where(d => d.HeadEmployeeId == id).ToListAsync())
            dept.HeadEmployeeId = null;

        var reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim();
        db.EmployeeHistories.Add(new EmployeeHistory
        {
            EmployeeId = id,
            EventType = status == EmploymentStatus.Terminated ? "Terminated" : "Separated",
            Description = reason is null
                ? $"Employee marked as {status} effective {sepDate:yyyy-MM-dd}"
                : $"Employee marked as {status}: {reason}",
            EffectiveDate = sepDate,
            ChangedByUserName = User.DisplayName()
        });

        audit.Log(AuditCategory.RecordChange, $"Separated employee {e.EmployeeCode} as {status}", nameof(Employee), id.ToString(), reason);
        await db.SaveChangesAsync();
        return Ok(new { message = $"{e.FullName} has been removed from active employees.", id, status = status.ToString(), separationDate = sepDate });
    }

    public record SeparateEmployeeRequest(string Status = "Resigned", DateOnly? SeparationDate = null, string? Reason = null);

    [HttpPost("{id:int}/photo")]
    public async Task<IActionResult> UploadPhoto(int id, IFormFile file)
    {
        if (!User.IsHr() && User.EmployeeId() != id) return Forbid();
        var e = await db.Employees.FindAsync(id);
        if (e is null) return NotFound();
        var dir = Path.Combine(env.ContentRootPath, "uploads", "photos");
        Directory.CreateDirectory(dir);
        var name = $"emp-{id}-{DateTime.UtcNow.Ticks}{Path.GetExtension(file.FileName)}";
        await using (var fs = System.IO.File.Create(Path.Combine(dir, name)))
            await file.CopyToAsync(fs);
        e.PhotoUrl = $"/uploads/photos/{name}";
        await db.SaveChangesAsync();
        return Ok(new { e.PhotoUrl });
    }

    // ---- Emergency contacts ----
    [HttpPost("{id:int}/contacts")]
    public async Task<IActionResult> AddContact(int id, EmergencyContact contact)
    {
        if (!User.IsHr() && User.EmployeeId() != id) return Forbid();
        contact.EmployeeId = id;
        contact.Id = 0;
        db.EmergencyContacts.Add(contact);
        await db.SaveChangesAsync();
        return Ok(contact);
    }

    [HttpDelete("contacts/{contactId:int}")]
    public async Task<IActionResult> DeleteContact(int contactId)
    {
        var c = await db.EmergencyContacts.FindAsync(contactId);
        if (c is null) return NotFound();
        if (!User.IsHr() && User.EmployeeId() != c.EmployeeId) return Forbid();
        db.EmergencyContacts.Remove(c);
        await db.SaveChangesAsync();
        return Ok();
    }

    // ---- Self-service: update own personal info ----
    [HttpPut("me/personal")]
    public async Task<IActionResult> UpdateMyInfo([FromBody] PersonalInfoUpdate input)
    {
        var eid = User.EmployeeId();
        if (eid is null) return BadRequest(new { message = "No employee profile linked to this account." });
        var e = await db.Employees.FindAsync(eid.Value);
        if (e is null) return NotFound();
        e.Address = input.Address ?? e.Address;
        e.ContactNumber = input.ContactNumber ?? e.ContactNumber;
        e.Email = input.Email ?? e.Email;
        e.CivilStatus = input.CivilStatus ?? e.CivilStatus;
        e.UpdatedAt = DateTime.UtcNow;
        audit.Log(AuditCategory.RecordChange, "Updated own personal information", nameof(Employee), e.Id.ToString());
        await db.SaveChangesAsync();
        return Ok(e);
    }

    public record PersonalInfoUpdate(string? Address, string? ContactNumber, string? Email, string? CivilStatus);

    // ---- Recurring payroll deductions ----
    [HttpGet("{id:int}/deductions")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.PayrollOfficer)}")]
    public async Task<IActionResult> ListDeductions(int id)
    {
        var emp = await db.Employees.FindAsync(id);
        if (emp is null) return NotFound();

        await deductions.SyncLoanDeductionsAsync(id);

        var items = await db.EmployeeDeductions
            .Include(d => d.DeductionType)
            .Include(d => d.Loan)
            .Where(d => d.EmployeeId == id)
            .OrderBy(d => d.DeductionType!.SortOrder)
            .Select(d => new
            {
                d.Id, d.DeductionTypeId, d.LoanId, d.Amount, d.RemainingBalance,
                d.TotalInstallments, d.PaidInstallments, d.IsProfileEnabled, d.IsActive,
                d.EffectiveFrom, d.EffectiveTo,
                frequency = d.Frequency.ToString(),
                typeCode = d.DeductionType!.Code,
                typeName = d.DeductionType.Name,
                loanReference = d.Loan != null ? d.Loan.Reference : null
            })
            .ToListAsync();

        return Ok(items);
    }

    public record EmployeeDeductionRequest(
        int DeductionTypeId, decimal Amount, DeductionFrequency Frequency,
        bool IsProfileEnabled = true, decimal? RemainingBalance = null, int? TotalInstallments = null,
        DateOnly? EffectiveFrom = null, DateOnly? EffectiveTo = null, int? LoanId = null);

    [HttpPost("{id:int}/deductions")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.PayrollOfficer)}")]
    public async Task<IActionResult> AddDeduction(int id, [FromBody] EmployeeDeductionRequest req)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == id)) return NotFound();
        if (!await db.DeductionTypes.AnyAsync(t => t.Id == req.DeductionTypeId && t.IsActive))
            return BadRequest(new { message = "Invalid deduction type." });

        var d = new EmployeeDeduction
        {
            EmployeeId = id,
            DeductionTypeId = req.DeductionTypeId,
            LoanId = req.LoanId,
            Amount = req.Amount,
            RemainingBalance = req.RemainingBalance ?? (req.TotalInstallments.HasValue ? req.Amount * req.TotalInstallments : null),
            TotalInstallments = req.TotalInstallments,
            Frequency = req.Frequency,
            IsProfileEnabled = req.IsProfileEnabled,
            IsActive = true,
            EffectiveFrom = req.EffectiveFrom,
            EffectiveTo = req.EffectiveTo
        };
        db.EmployeeDeductions.Add(d);
        audit.Log(AuditCategory.Payroll, $"Added recurring deduction for employee #{id}", nameof(EmployeeDeduction));
        await db.SaveChangesAsync();
        return Ok(d);
    }

    [HttpPut("{id:int}/deductions/{deductionId:int}")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.PayrollOfficer)}")]
    public async Task<IActionResult> UpdateDeduction(int id, int deductionId, [FromBody] EmployeeDeductionRequest req)
    {
        var d = await db.EmployeeDeductions.FirstOrDefaultAsync(x => x.Id == deductionId && x.EmployeeId == id);
        if (d is null) return NotFound();

        d.DeductionTypeId = req.DeductionTypeId;
        d.Amount = req.Amount;
        d.Frequency = req.Frequency;
        d.IsProfileEnabled = req.IsProfileEnabled;
        d.RemainingBalance = req.RemainingBalance;
        d.TotalInstallments = req.TotalInstallments;
        d.EffectiveFrom = req.EffectiveFrom;
        d.EffectiveTo = req.EffectiveTo;
        d.LoanId = req.LoanId;
        d.UpdatedAt = DateTime.UtcNow;
        audit.Log(AuditCategory.Payroll, $"Updated recurring deduction #{deductionId} for employee #{id}", nameof(EmployeeDeduction), deductionId.ToString());
        await db.SaveChangesAsync();
        return Ok(d);
    }

    [HttpDelete("{id:int}/deductions/{deductionId:int}")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.PayrollOfficer)}")]
    public async Task<IActionResult> RemoveDeduction(int id, int deductionId)
    {
        var d = await db.EmployeeDeductions.FirstOrDefaultAsync(x => x.Id == deductionId && x.EmployeeId == id);
        if (d is null) return NotFound();
        d.IsActive = false;
        d.IsProfileEnabled = false;
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("{id:int}/deductions/apply-template/{templateId:int}")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.PayrollOfficer)}")]
    public async Task<IActionResult> ApplyDeductionTemplate(int id, int templateId, [FromBody] ApplyTemplateRequest? req)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == id)) return NotFound();
        var template = await db.DeductionTemplates.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == templateId && t.IsActive);
        if (template is null) return NotFound();

        var typeIds = template.Items.Select(i => i.DeductionTypeId).ToHashSet();
        var existing = await db.EmployeeDeductions
            .Where(d => d.EmployeeId == id && d.IsActive && typeIds.Contains(d.DeductionTypeId))
            .Select(d => d.DeductionTypeId)
            .ToHashSetAsync();

        var added = 0;
        foreach (var item in template.Items)
        {
            if (existing.Contains(item.DeductionTypeId)) continue;
            db.EmployeeDeductions.Add(new EmployeeDeduction
            {
                EmployeeId = id,
                DeductionTypeId = item.DeductionTypeId,
                Amount = req?.DefaultAmount ?? 0,
                Frequency = req?.Frequency ?? DeductionFrequency.EveryCutoff,
                IsProfileEnabled = true,
                IsActive = true
            });
            added++;
        }
        await db.SaveChangesAsync();
        return Ok(new { added, message = $"Template applied — {added} deduction(s) added." });
    }

    public record ApplyTemplateRequest(decimal DefaultAmount = 0, DeductionFrequency Frequency = DeductionFrequency.EveryCutoff);

    /// <summary>HR policy: monthly salary ÷ 24 working days = daily rate.</summary>
    private static void SyncCompensationRates(Employee e)
    {
        if (e.MonthlySalary > 0)
            e.DailyRate = Math.Round(e.MonthlySalary / 24m, 2);
    }

    /// <summary>New hires receive 10 days Emergency Leave for the hire year.</summary>
    private async Task EnsureEmergencyLeaveBalanceAsync(Employee employee, int year)
    {
        var el = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Code == "EL" && t.IsActive);
        if (el is null) return;

        var existing = await db.LeaveBalances.FirstOrDefaultAsync(b =>
            b.EmployeeId == employee.Id && b.LeaveTypeId == el.Id && b.Year == year);
        if (existing is null)
        {
            db.LeaveBalances.Add(new LeaveBalance
            {
                Employee = employee,
                LeaveTypeId = el.Id,
                Year = year,
                Credits = el.DefaultAnnualCredits > 0 ? el.DefaultAnnualCredits : 10
            });
        }
        else if (existing.Credits < 10)
            existing.Credits = 10;
    }

    /// <summary>SIL (5 days) is granted when an employee becomes Regular.</summary>
    private async Task EnsureSilBalanceAsync(int employeeId, int year)
    {
        var emp = await db.Employees.FindAsync(employeeId);
        if (emp is null || emp.Status != EmploymentStatus.Regular) return;

        var sil = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Code == "SIL" && t.IsActive);
        if (sil is null) return;

        var existing = await db.LeaveBalances.FirstOrDefaultAsync(b =>
            b.EmployeeId == employeeId && b.LeaveTypeId == sil.Id && b.Year == year);
        if (existing is null)
            db.LeaveBalances.Add(new LeaveBalance { EmployeeId = employeeId, LeaveTypeId = sil.Id, Year = year, Credits = sil.DefaultAnnualCredits });
    }
}
