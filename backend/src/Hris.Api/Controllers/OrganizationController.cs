using Hris.Api.Data;
using Hris.Api.Services;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Controllers;

[ApiController]
[Route("api/organization")]
[Authorize]
public class OrganizationController(HrisDbContext db, AuditService audit) : ControllerBase
{
    private const string AdminRoles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)}";

    // ---- Company ----
    [HttpGet("company")]
    public async Task<IActionResult> GetCompany() => Ok(await db.Companies.FirstOrDefaultAsync());

    [HttpPut("company")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> UpdateCompany(Company input)
    {
        var c = await db.Companies.FirstOrDefaultAsync();
        if (c is null) { db.Companies.Add(input); }
        else { input.Id = c.Id; db.Entry(c).CurrentValues.SetValues(input); }
        audit.Log(AuditCategory.RecordChange, "Updated company profile", nameof(Company));
        await db.SaveChangesAsync();
        return Ok(c ?? input);
    }

    // ---- Branches ----
    [HttpGet("branches")]
    public async Task<IActionResult> Branches() =>
        Ok(await db.Branches.Include(b => b.Sites).OrderBy(b => b.Name).ToListAsync());

    [HttpPost("branches")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> CreateBranch(Branch b)
    {
        b.CompanyId = (await db.Companies.FirstAsync()).Id;
        b.Company = null;
        db.Branches.Add(b);
        audit.Log(AuditCategory.RecordChange, $"Created branch {b.Name}", nameof(Branch));
        await db.SaveChangesAsync();
        return Ok(b);
    }

    [HttpPut("branches/{id:int}")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> UpdateBranch(int id, Branch input)
    {
        var b = await db.Branches.FindAsync(id);
        if (b is null) return NotFound();
        b.Code = input.Code; b.Name = input.Name; b.Address = input.Address;
        b.ContactNumber = input.ContactNumber; b.IsActive = input.IsActive;
        b.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(b);
    }

    // ---- Sites ----
    [HttpGet("sites")]
    public async Task<IActionResult> Sites() =>
        Ok(await db.Sites.Include(s => s.Branch).Include(s => s.Devices).OrderBy(s => s.Name).ToListAsync());

    [HttpPost("sites")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> CreateSite(Site s)
    {
        s.Branch = null;
        s.GatewayApiKey = Guid.NewGuid().ToString("N");
        db.Sites.Add(s);
        audit.Log(AuditCategory.RecordChange, $"Created site {s.Name}", nameof(Site));
        await db.SaveChangesAsync();
        return Ok(s);
    }

    [HttpPut("sites/{id:int}")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> UpdateSite(int id, Site input)
    {
        var s = await db.Sites.FindAsync(id);
        if (s is null) return NotFound();
        s.Code = input.Code; s.Name = input.Name; s.Address = input.Address;
        s.BranchId = input.BranchId; s.IsActive = input.IsActive;
        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(s);
    }

    [HttpPost("sites/{id:int}/regenerate-key")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> RegenerateKey(int id)
    {
        var s = await db.Sites.FindAsync(id);
        if (s is null) return NotFound();
        s.GatewayApiKey = Guid.NewGuid().ToString("N");
        audit.Log(AuditCategory.Security, $"Regenerated gateway API key for site {s.Name}", nameof(Site), id.ToString());
        await db.SaveChangesAsync();
        return Ok(new { s.GatewayApiKey });
    }

    // ---- Departments ----
    [HttpGet("departments")]
    public async Task<IActionResult> Departments() =>
        Ok(await db.Departments.Include(d => d.Branch).Include(d => d.HeadEmployee).OrderBy(d => d.Name).ToListAsync());

    [HttpPost("departments")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> CreateDepartment(Department d)
    {
        d.Branch = null; d.HeadEmployee = null;
        db.Departments.Add(d);
        audit.Log(AuditCategory.RecordChange, $"Created department {d.Name}", nameof(Department));
        await db.SaveChangesAsync();
        return Ok(d);
    }

    [HttpPut("departments/{id:int}")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> UpdateDepartment(int id, Department input)
    {
        var d = await db.Departments.FindAsync(id);
        if (d is null) return NotFound();
        d.Code = input.Code; d.Name = input.Name; d.BranchId = input.BranchId;
        d.HeadEmployeeId = input.HeadEmployeeId; d.IsActive = input.IsActive;
        d.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(d);
    }

    [HttpDelete("departments/{id:int}")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> DeleteDepartment(int id)
    {
        var d = await db.Departments.FindAsync(id);
        if (d is null) return NotFound();

        var assignedEmployees = await db.Employees.Where(e => e.DepartmentId == id).ToListAsync();
        var activeLinkedEmployeeIds = await db.Users
            .Where(u => u.IsActive && u.EmployeeId != null)
            .Select(u => u.EmployeeId!.Value)
            .ToHashSetAsync();

        var blocked = assignedEmployees.Where(e =>
            activeLinkedEmployeeIds.Contains(e.Id) &&
            e.Status is not (EmploymentStatus.Resigned or EmploymentStatus.Terminated or EmploymentStatus.Retired)).ToList();
        if (blocked.Count > 0)
        {
            var names = string.Join(", ", blocked.Select(e => e.FullName));
            return BadRequest(new { message = $"Cannot delete: {blocked.Count} active employee(s) with login accounts still belong to this department ({names}). Delete or disable those user accounts first, or reassign the employees." });
        }

        foreach (var e in assignedEmployees)
        {
            e.DepartmentId = null;
            e.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var p in await db.Positions.Where(p => p.DepartmentId == id).ToListAsync())
        {
            p.DepartmentId = null;
            p.UpdatedAt = DateTime.UtcNow;
        }

        if (d.HeadEmployeeId.HasValue)
            d.HeadEmployeeId = null;

        db.Departments.Remove(d);
        audit.Log(AuditCategory.RecordChange, $"Deleted department {d.Name}", nameof(Department), id.ToString());
        await db.SaveChangesAsync();
        return Ok(new { message = "Department deleted permanently. Unlinked employee and position records were kept." });
    }

    // ---- Positions ----
    [HttpGet("positions")]
    public async Task<IActionResult> Positions() =>
        Ok(await db.Positions.Include(p => p.Department).OrderBy(p => p.Title).ToListAsync());

    [HttpPost("positions")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> CreatePosition(Position p)
    {
        p.Department = null;
        db.Positions.Add(p);
        await db.SaveChangesAsync();
        return Ok(p);
    }

    [HttpPut("positions/{id:int}")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> UpdatePosition(int id, Position input)
    {
        var p = await db.Positions.FindAsync(id);
        if (p is null) return NotFound();
        p.Code = input.Code; p.Title = input.Title; p.Description = input.Description;
        p.DepartmentId = input.DepartmentId; p.MinSalary = input.MinSalary; p.MaxSalary = input.MaxSalary;
        p.IsActive = input.IsActive;
        p.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(p);
    }

    [HttpDelete("positions/{id:int}")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> DeletePosition(int id)
    {
        var p = await db.Positions.FindAsync(id);
        if (p is null) return NotFound();

        var assignedEmployees = await db.Employees.Where(e => e.PositionId == id).ToListAsync();
        var activeLinkedEmployeeIds = await db.Users
            .Where(u => u.IsActive && u.EmployeeId != null)
            .Select(u => u.EmployeeId!.Value)
            .ToHashSetAsync();

        var blocked = assignedEmployees.Where(e =>
            activeLinkedEmployeeIds.Contains(e.Id) &&
            e.Status is not (EmploymentStatus.Resigned or EmploymentStatus.Terminated or EmploymentStatus.Retired)).ToList();
        if (blocked.Count > 0)
        {
            var names = string.Join(", ", blocked.Select(e => e.FullName));
            return BadRequest(new { message = $"Cannot delete: {blocked.Count} active employee(s) with login accounts still hold this position ({names}). Delete or disable those user accounts first, or reassign the employees." });
        }

        foreach (var e in assignedEmployees)
        {
            e.PositionId = null;
            e.UpdatedAt = DateTime.UtcNow;
        }

        db.Positions.Remove(p);
        audit.Log(AuditCategory.RecordChange, $"Deleted position {p.Title}", nameof(Position), id.ToString());
        await db.SaveChangesAsync();
        return Ok(new { message = "Position deleted permanently. Employee records without active login accounts were unlinked." });
    }

    // ---- Holidays ----
    [HttpGet("holidays")]
    public async Task<IActionResult> Holidays([FromQuery] int? year)
    {
        var q = db.Holidays.AsQueryable();
        if (year.HasValue) q = q.Where(h => h.Date.Year == year);
        return Ok(await q.OrderBy(h => h.Date).ToListAsync());
    }

    [HttpPost("holidays")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> CreateHoliday(Holiday h)
    {
        db.Holidays.Add(h);
        await db.SaveChangesAsync();
        return Ok(h);
    }

    [HttpDelete("holidays/{id:int}")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> DeleteHoliday(int id)
    {
        var h = await db.Holidays.FindAsync(id);
        if (h is null) return NotFound();
        db.Holidays.Remove(h);
        await db.SaveChangesAsync();
        return Ok();
    }

    // ---- Reporting hierarchy ----
    [HttpGet("hierarchy")]
    public async Task<IActionResult> Hierarchy()
    {
        var emps = await db.Employees
            .Where(e => e.Status != EmploymentStatus.Resigned && e.Status != EmploymentStatus.Terminated)
            .Select(e => new { e.Id, name = e.FirstName + " " + e.LastName, position = e.Position!.Title, department = e.Department!.Name, e.ManagerId })
            .ToListAsync();
        return Ok(emps);
    }
}
