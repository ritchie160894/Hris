using Hris.Api.Data;
using Hris.Api.Services;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Controllers;

// ---------------- Government Contributions ----------------

[ApiController]
[Route("api/government")]
[Authorize]
public class GovernmentController(HrisDbContext db) : ControllerBase
{
    [HttpGet("sss")]
    public async Task<IActionResult> Sss() => Ok(await db.SssBrackets.OrderBy(s => s.RangeFrom).ToListAsync());

    [HttpGet("philhealth")]
    public async Task<IActionResult> PhilHealth() => Ok(await db.PhilHealthConfigs.OrderByDescending(p => p.EffectiveYear).ToListAsync());

    [HttpGet("pagibig")]
    public async Task<IActionResult> PagIbig() => Ok(await db.PagIbigConfigs.OrderByDescending(p => p.EffectiveYear).ToListAsync());

    [HttpGet("tax")]
    public async Task<IActionResult> Tax() => Ok(await db.TaxBrackets.OrderBy(t => t.RangeFrom).ToListAsync());

    /// <summary>Monthly remittance summary per agency from finalized payslips.</summary>
    [HttpGet("remittance")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.PayrollOfficer)}")]
    public async Task<IActionResult> Remittance([FromQuery] int year, [FromQuery] int month, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var execIds = await ExecutiveExemption.GetExemptEmployeeIdsAsync(db);
        var q = db.Payslips.AsNoTracking()
            .Where(p => !execIds.Contains(p.EmployeeId))
            .Where(p => p.PayrollCutoff != null
                && p.PayrollCutoff.PeriodStart >= first
                && p.PayrollCutoff.PeriodEnd <= last
                && (p.PayrollCutoff.Status == PayrollStatus.Approved
                    || p.PayrollCutoff.Status == PayrollStatus.Released
                    || p.PayrollCutoff.Status == PayrollStatus.Closed))
            .GroupBy(p => new { p.EmployeeId, p.Employee!.EmployeeCode, p.Employee.FirstName, p.Employee.LastName, p.Employee.SssNumber, p.Employee.PhilHealthNumber, p.Employee.PagIbigNumber, p.Employee.Tin })
            .Select(g => new
            {
                g.Key.EmployeeCode,
                name = g.Key.FirstName + " " + g.Key.LastName,
                g.Key.SssNumber, g.Key.PhilHealthNumber, g.Key.PagIbigNumber, g.Key.Tin,
                sssEe = g.Sum(p => p.SssEmployee),
                sssEr = g.Sum(p => p.SssEmployer),
                philHealth = g.Sum(p => p.PhilHealthEmployee + p.PhilHealthEmployer),
                pagIbig = g.Sum(p => p.PagIbigEmployee + p.PagIbigEmployer),
                tax = g.Sum(p => p.WithholdingTax)
            })
            .OrderBy(r => r.EmployeeCode);

        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }
}

// ---------------- Benefits ----------------

[ApiController]
[Route("api/benefits")]
[Authorize]
public class BenefitsController(HrisDbContext db) : ControllerBase
{
    private const string HrRoles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)}";
    private const string BenefitsRoles = $"{HrRoles},{nameof(UserRole.PayrollOfficer)}";

    [HttpGet]
    [Authorize(Roles = BenefitsRoles)]
    public async Task<IActionResult> List() => Ok(await db.Benefits.Where(b => b.IsActive).ToListAsync());

    [HttpPost]
    [Authorize(Roles = HrRoles)]
    public async Task<IActionResult> Create(Benefit b) { db.Benefits.Add(b); await db.SaveChangesAsync(); return Ok(b); }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = HrRoles)]
    public async Task<IActionResult> Delete(int id)
    {
        var b = await db.Benefits.FindAsync(id);
        if (b is null || !b.IsActive) return NotFound();
        var hasAssignments = await db.EmployeeBenefits.AnyAsync(e => e.BenefitId == id);
        if (hasAssignments)
            return BadRequest(new { message = "Remove all employee assignments for this benefit before deleting it." });
        b.IsActive = false;
        await db.SaveChangesAsync();
        return Ok(new { message = "Benefit deleted." });
    }

    [HttpGet("assignments")]
    [Authorize(Roles = BenefitsRoles)]
    public async Task<IActionResult> Assignments([FromQuery] int? employeeId)
    {
        var q = db.EmployeeBenefits.Include(e => e.Benefit).Include(e => e.Employee).AsQueryable();
        if (employeeId.HasValue) q = q.Where(e => e.EmployeeId == employeeId);
        return Ok(await q.Select(e => new
        {
            e.Id, e.EffectiveDate, e.EndDate, e.PolicyNumber, e.Notes,
            benefit = new { e.Benefit!.Id, e.Benefit.Name, type = e.Benefit.Type.ToString(), e.Benefit.Provider },
            employee = new { e.Employee!.Id, e.Employee.EmployeeCode, name = e.Employee.FirstName + " " + e.Employee.LastName }
        }).ToListAsync());
    }

    [HttpPost("assignments")]
    [Authorize(Roles = HrRoles)]
    public async Task<IActionResult> Assign(EmployeeBenefit e)
    {
        e.Benefit = null; e.Employee = null;
        db.EmployeeBenefits.Add(e);
        await db.SaveChangesAsync();
        return Ok(e);
    }

    [HttpDelete("assignments/{id:int}")]
    [Authorize(Roles = HrRoles)]
    public async Task<IActionResult> Unassign(int id)
    {
        var e = await db.EmployeeBenefits.FindAsync(id);
        if (e is null) return NotFound();
        db.EmployeeBenefits.Remove(e);
        await db.SaveChangesAsync();
        return Ok();
    }
}

// ---------------- Performance ----------------

[ApiController]
[Route("api/performance")]
[Authorize]
public class PerformanceController(HrisDbContext db) : ControllerBase
{
    [HttpGet("reviews")]
    public async Task<IActionResult> Reviews([FromQuery] int? employeeId)
    {
        var q = db.PerformanceReviews.Include(r => r.Employee).Include(r => r.KpiScores).AsQueryable();
        if (User.Role() == UserRole.Employee) q = q.Where(r => r.EmployeeId == User.EmployeeId());
        else if (employeeId.HasValue) q = q.Where(r => r.EmployeeId == employeeId);
        return Ok(await q.OrderByDescending(r => r.ReviewDate)
            .Select(r => new
            {
                r.Id, r.Period, r.ReviewerName, r.ReviewDate, r.OverallScore, r.Strengths, r.AreasForImprovement, r.Comments, r.IsFinalized,
                employee = new { r.Employee!.Id, r.Employee.EmployeeCode, name = r.Employee.FirstName + " " + r.Employee.LastName },
                kpis = r.KpiScores.Select(k => new { k.Id, k.KpiName, k.Weight, k.Score, k.Remarks })
            }).ToListAsync());
    }

    [HttpPost("reviews")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)},{nameof(UserRole.DepartmentHead)},{nameof(UserRole.Supervisor)}")]
    public async Task<IActionResult> Create(PerformanceReview r)
    {
        r.Employee = null;
        if (r.KpiScores.Count > 0)
        {
            var totalWeight = r.KpiScores.Sum(k => k.Weight);
            if (totalWeight > 0)
                r.OverallScore = Math.Round(r.KpiScores.Sum(k => k.Score * k.Weight) / totalWeight, 2);
        }
        db.PerformanceReviews.Add(r);
        await db.SaveChangesAsync();
        return Ok(r);
    }
}

// ---------------- Training ----------------

[ApiController]
[Route("api/training")]
[Authorize]
public class TrainingController(HrisDbContext db) : ControllerBase
{
    private const string HrRoles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)}";

    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await db.Trainings.Include(t => t.Participants).ThenInclude(p => p.Employee)
            .OrderByDescending(t => t.StartDate)
            .Select(t => new
            {
                t.Id, t.Title, t.Provider, t.Description, t.StartDate, t.EndDate, t.Location,
                status = t.Status.ToString(), t.Cost,
                participants = t.Participants.Select(p => new
                {
                    p.Id, p.Completed, p.CertificateNumber, p.CertificateExpiry,
                    employee = new { p.Employee!.Id, p.Employee.EmployeeCode, name = p.Employee.FirstName + " " + p.Employee.LastName }
                })
            }).ToListAsync());

    [HttpPost]
    [Authorize(Roles = HrRoles)]
    public async Task<IActionResult> Create(Training t) { db.Trainings.Add(t); await db.SaveChangesAsync(); return Ok(t); }

    [HttpPut("{id:int}")]
    [Authorize(Roles = HrRoles)]
    public async Task<IActionResult> Update(int id, Training input)
    {
        var t = await db.Trainings.FindAsync(id);
        if (t is null) return NotFound();
        t.Title = input.Title; t.Provider = input.Provider; t.Description = input.Description;
        t.StartDate = input.StartDate; t.EndDate = input.EndDate; t.Location = input.Location;
        t.Status = input.Status; t.Cost = input.Cost;
        await db.SaveChangesAsync();
        return Ok(t);
    }

    [HttpPost("{id:int}/participants")]
    [Authorize(Roles = HrRoles)]
    public async Task<IActionResult> AddParticipant(int id, TrainingParticipant p)
    {
        p.TrainingId = id; p.Training = null; p.Employee = null;
        db.TrainingParticipants.Add(p);
        await db.SaveChangesAsync();
        return Ok(p);
    }

    [HttpPut("participants/{id:int}")]
    [Authorize(Roles = HrRoles)]
    public async Task<IActionResult> UpdateParticipant(int id, TrainingParticipant input)
    {
        var p = await db.TrainingParticipants.FindAsync(id);
        if (p is null) return NotFound();
        p.Completed = input.Completed; p.CertificateNumber = input.CertificateNumber; p.CertificateExpiry = input.CertificateExpiry;
        await db.SaveChangesAsync();
        return Ok(p);
    }

    /// <summary>Certifications expiring within 90 days.</summary>
    [HttpGet("expiring-certifications")]
    public async Task<IActionResult> Expiring()
    {
        var limit = DateOnly.FromDateTime(DateTime.Today.AddDays(90));
        return Ok(await db.TrainingParticipants.Include(p => p.Employee).Include(p => p.Training)
            .Where(p => p.CertificateExpiry != null && p.CertificateExpiry <= limit)
            .Select(p => new
            {
                p.Id, p.CertificateNumber, p.CertificateExpiry, training = p.Training!.Title,
                employee = new { p.Employee!.Id, name = p.Employee.FirstName + " " + p.Employee.LastName }
            }).ToListAsync());
    }
}

// ---------------- Announcements ----------------

[ApiController]
[Route("api/announcements")]
[Authorize]
public class AnnouncementsController(HrisDbContext db, NotificationService notifications) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = db.Announcements.Where(a => a.IsActive);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.PublishDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpPost]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)}")]
    public async Task<IActionResult> Create(Announcement a)
    {
        a.PostedByName = User.DisplayName();
        db.Announcements.Add(a);
        await db.SaveChangesAsync();

        // notify all active users in-app
        var userIds = await db.Users.Where(u => u.IsActive).Select(u => u.Id).ToListAsync();
        foreach (var uid in userIds)
            notifications.Notify(uid, NotificationType.Announcement, a.Title, a.Body.Length > 140 ? a.Body[..140] + "…" : a.Body);
        await db.SaveChangesAsync();
        return Ok(a);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)}")]
    public async Task<IActionResult> Delete(int id)
    {
        var a = await db.Announcements.FindAsync(id);
        if (a is null) return NotFound();
        a.IsActive = false;
        await db.SaveChangesAsync();
        return Ok();
    }
}

// ---------------- Notifications ----------------

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController(HrisDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Mine([FromQuery] bool unreadOnly = false, [FromQuery] int? page = null, [FromQuery] int pageSize = 25, [FromQuery] int take = 50)
    {
        var q = db.Notifications.Where(n => n.UserId == User.UserId());
        if (unreadOnly) q = q.Where(n => !n.IsRead);
        var unread = await db.Notifications.CountAsync(n => n.UserId == User.UserId() && !n.IsRead);

        if (page.HasValue)
        {
            pageSize = Math.Clamp(pageSize, 1, 100);
            var total = await q.CountAsync();
            var items = await q.OrderByDescending(n => n.CreatedAt)
                .Skip((page.Value - 1) * pageSize).Take(pageSize)
                .Select(n => new { n.Id, type = n.Type.ToString(), n.Title, n.Message, n.LinkUrl, n.IsRead, n.CreatedAt })
                .ToListAsync();
            return Ok(new { unread, total, page = page.Value, pageSize, items });
        }

        var recent = await q.OrderByDescending(n => n.CreatedAt).Take(take)
            .Select(n => new { n.Id, type = n.Type.ToString(), n.Title, n.Message, n.LinkUrl, n.IsRead, n.CreatedAt })
            .ToListAsync();
        return Ok(new { unread, items = recent });
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == User.UserId());
        if (n is null) return NotFound();
        n.IsRead = true;
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        await db.Notifications.Where(n => n.UserId == User.UserId() && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == User.UserId());
        if (n is null) return NotFound();
        db.Notifications.Remove(n);
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("delete-read")]
    public async Task<IActionResult> DeleteRead()
    {
        await db.Notifications.Where(n => n.UserId == User.UserId() && n.IsRead).ExecuteDeleteAsync();
        return Ok();
    }
}

// ---------------- Users & Audit ----------------

[ApiController]
[Route("api/users")]
[Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)}")]
public class UsersController(HrisDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search, [FromQuery] string? role, [FromQuery] int? departmentId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var q = db.Users.Include(u => u.Employee).ThenInclude(e => e!.Department).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(u => u.Username.Contains(search) || u.DisplayName.Contains(search)
                || (u.Email != null && u.Email.Contains(search)));
        if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<UserRole>(role, out var roleFilter))
            q = q.Where(u => u.Role == roleFilter);
        if (departmentId.HasValue)
            q = q.Where(u => u.Employee != null && u.Employee.DepartmentId == departmentId);

        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);
        var total = await q.CountAsync();
        var items = await q.OrderBy(u => u.Username)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new
            {
                u.Id, u.Username, u.DisplayName, role = u.Role.ToString(), u.Email,
                u.IsActive, u.IsLocked, u.LastLoginAt, u.EmployeeId,
                employee = u.Employee == null ? null : u.Employee.FirstName + " " + u.Employee.LastName,
                employeeCode = u.Employee == null ? null : u.Employee.EmployeeCode,
                departmentId = u.Employee == null ? (int?)null : u.Employee.DepartmentId,
                department = u.Employee == null || u.Employee.Department == null ? null : u.Employee.Department.Name
            }).ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("employee-options")]
    public async Task<IActionResult> EmployeeOptions([FromQuery] int? departmentId, [FromQuery] int? forUserId)
    {
        var takenIds = await db.Users
            .Where(u => u.EmployeeId != null && u.Id != forUserId)
            .Select(u => u.EmployeeId!.Value)
            .ToListAsync();

        var q = db.Employees.Include(e => e.Department)
            .Where(e => e.Status != EmploymentStatus.Resigned && e.Status != EmploymentStatus.Terminated && e.Status != EmploymentStatus.Retired
                && !takenIds.Contains(e.Id));
        if (departmentId.HasValue) q = q.Where(e => e.DepartmentId == departmentId);

        var items = await q.OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .Select(e => new
            {
                e.Id, e.EmployeeCode,
                name = e.FirstName + " " + e.LastName,
                departmentId = e.DepartmentId,
                department = e.Department == null ? null : e.Department.Name
            }).ToListAsync();
        return Ok(items);
    }

    public record CreateUserRequest(string Username, string Password, string DisplayName, string Role, int? EmployeeId, string? Email);

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest req)
    {
        if (await db.Users.AnyAsync(u => u.Username == req.Username))
            return BadRequest(new { message = "Username already taken." });
        if (!Enum.TryParse<UserRole>(req.Role, out var role))
            return BadRequest(new { message = "Invalid role." });
        var user = new User
        {
            Username = req.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            DisplayName = req.DisplayName,
            Role = role,
            EmployeeId = req.EmployeeId,
            Email = req.Email
        };
        db.Users.Add(user);
        audit.Log(AuditCategory.Security, $"Created user '{req.Username}' with role {role}", nameof(User));
        await ApplyDepartmentHeadSyncAsync(role, req.EmployeeId);
        await db.SaveChangesAsync();
        return Ok(new { user.Id, user.Username });
    }

    public record UpdateUserRequest(string DisplayName, string Role, int? EmployeeId, string? Email, bool IsActive, string? NewPassword);

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateUserRequest req)
    {
        var u = await db.Users.FindAsync(id);
        if (u is null) return NotFound();
        var prevRole = u.Role;
        var prevEmployeeId = u.EmployeeId;
        if (Enum.TryParse<UserRole>(req.Role, out var role)) u.Role = role;
        u.DisplayName = req.DisplayName;
        u.EmployeeId = req.EmployeeId;
        u.Email = req.Email;
        u.IsActive = req.IsActive;
        if (!string.IsNullOrEmpty(req.NewPassword))
            u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        audit.Log(AuditCategory.Security, $"Updated user '{u.Username}'", nameof(User), id.ToString());
        await ApplyDepartmentHeadSyncAsync(u.Role, u.EmployeeId, prevRole, prevEmployeeId);
        await db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>When a user is Department Head, set their employee's department official head on the org chart.</summary>
    private async Task ApplyDepartmentHeadSyncAsync(UserRole role, int? employeeId, UserRole? previousRole = null, int? previousEmployeeId = null)
    {
        if (previousRole == UserRole.DepartmentHead && previousEmployeeId.HasValue
            && (role != UserRole.DepartmentHead || previousEmployeeId != employeeId))
        {
            foreach (var dept in await db.Departments.Where(d => d.HeadEmployeeId == previousEmployeeId).ToListAsync())
            {
                dept.HeadEmployeeId = null;
                dept.UpdatedAt = DateTime.UtcNow;
            }
        }

        if (role != UserRole.DepartmentHead || !employeeId.HasValue) return;

        var emp = await db.Employees.FindAsync(employeeId.Value);
        if (emp?.DepartmentId is null) return;

        var department = await db.Departments.FindAsync(emp.DepartmentId);
        if (department is null) return;

        department.HeadEmployeeId = emp.Id;
        department.UpdatedAt = DateTime.UtcNow;
    }

    [HttpPost("{id:int}/unlock")]
    public async Task<IActionResult> Unlock(int id)
    {
        var u = await db.Users.FindAsync(id);
        if (u is null) return NotFound();
        u.IsLocked = false;
        u.FailedLoginAttempts = 0;
        audit.Log(AuditCategory.Security, $"Unlocked user '{u.Username}'", nameof(User), id.ToString());
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (id == User.UserId())
            return BadRequest(new { message = "You cannot delete your own account while logged in." });

        var u = await db.Users.FindAsync(id);
        if (u is null) return NotFound();

        if (u.Role == UserRole.SuperAdministrator)
        {
            var otherAdmins = await db.Users.CountAsync(x => x.Role == UserRole.SuperAdministrator && x.Id != id);
            if (otherAdmins == 0)
                return BadRequest(new { message = "Cannot delete the last Super Administrator account." });
        }

        await db.Notifications.Where(n => n.UserId == id).ExecuteDeleteAsync();
        db.Users.Remove(u);
        audit.Log(AuditCategory.Security, $"Deleted user '{u.Username}' permanently", nameof(User), id.ToString());
        await db.SaveChangesAsync();
        return Ok(new { message = $"User '{u.Username}' deleted permanently." });
    }
}

[ApiController]
[Route("api/audit")]
[Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)}")]
public class AuditController(HrisDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? category, [FromQuery] string? search,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var q = db.AuditLogs.AsQueryable();
        if (!string.IsNullOrEmpty(category) && Enum.TryParse<AuditCategory>(category, out var cat)) q = q.Where(a => a.Category == cat);
        if (!string.IsNullOrEmpty(search)) q = q.Where(a => a.Action.Contains(search) || (a.UserName != null && a.UserName.Contains(search)));
        if (from.HasValue) q = q.Where(a => a.CreatedAt >= from.Value.ToDateTime(TimeOnly.MinValue));
        if (to.HasValue) q = q.Where(a => a.CreatedAt <= to.Value.ToDateTime(TimeOnly.MaxValue));
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new { a.Id, category = a.Category.ToString(), a.UserName, a.Action, a.EntityType, a.EntityId, a.Details, a.IpAddress, a.CreatedAt })
            .ToListAsync();
        return Ok(new { total, items });
    }
}

// ---------------- Documents ----------------

[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController(HrisDbContext db, IWebHostEnvironment env, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? employeeId, [FromQuery] string? category, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var q = db.EmployeeDocuments.Include(d => d.Employee).AsQueryable();
        if (User.Role() == UserRole.Employee) q = q.Where(d => d.EmployeeId == User.EmployeeId());
        else if (employeeId.HasValue) q = q.Where(d => d.EmployeeId == employeeId);
        if (!string.IsNullOrEmpty(category) && Enum.TryParse<DocumentCategory>(category, out var cat)) q = q.Where(d => d.Category == cat);

        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new
            {
                d.Id, category = d.Category.ToString(), d.Title, d.FileName, d.FileSize, d.ExpiryDate, d.Notes, d.CreatedAt,
                employee = new { d.Employee!.Id, d.Employee.EmployeeCode, name = d.Employee.FirstName + " " + d.Employee.LastName }
            }).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpPost("upload")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)}")]
    public async Task<IActionResult> Upload([FromForm] int employeeId, [FromForm] string title, [FromForm] string category,
        [FromForm] DateOnly? expiryDate, [FromForm] string? notes, IFormFile file)
    {
        var dir = Path.Combine(env.ContentRootPath, "uploads", "documents");
        Directory.CreateDirectory(dir);
        var stored = $"doc-{employeeId}-{DateTime.UtcNow.Ticks}{Path.GetExtension(file.FileName)}";
        await using (var fs = System.IO.File.Create(Path.Combine(dir, stored)))
            await file.CopyToAsync(fs);

        var doc = new EmployeeDocument
        {
            EmployeeId = employeeId,
            Title = title,
            Category = Enum.TryParse<DocumentCategory>(category, out var cat) ? cat : DocumentCategory.Other,
            FileName = file.FileName,
            FilePath = $"/uploads/documents/{stored}",
            FileSize = file.Length,
            ExpiryDate = expiryDate,
            Notes = notes
        };
        db.EmployeeDocuments.Add(doc);
        audit.Log(AuditCategory.RecordChange, $"Uploaded document '{title}' for employee #{employeeId}", nameof(EmployeeDocument));
        await db.SaveChangesAsync();
        return Ok(doc);
    }

    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Download(int id)
    {
        var d = await db.EmployeeDocuments.FindAsync(id);
        if (d is null) return NotFound();
        if (User.Role() == UserRole.Employee && d.EmployeeId != User.EmployeeId()) return Forbid();
        var path = Path.Combine(env.ContentRootPath, d.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(path)) return NotFound(new { message = "File missing on server." });
        return PhysicalFile(path, "application/octet-stream", d.FileName);
    }

    /// <summary>Documents expiring within the next 60 days (contract/certification monitoring).</summary>
    [HttpGet("expiring")]
    public async Task<IActionResult> Expiring()
    {
        var limit = DateOnly.FromDateTime(DateTime.Today.AddDays(60));
        return Ok(await db.EmployeeDocuments.Include(d => d.Employee)
            .Where(d => d.ExpiryDate != null && d.ExpiryDate <= limit)
            .OrderBy(d => d.ExpiryDate)
            .Select(d => new
            {
                d.Id, d.Title, category = d.Category.ToString(), d.ExpiryDate,
                employee = new { d.Employee!.Id, name = d.Employee.FirstName + " " + d.Employee.LastName }
            }).ToListAsync());
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)}")]
    public async Task<IActionResult> Delete(int id)
    {
        var d = await db.EmployeeDocuments.FindAsync(id);
        if (d is null) return NotFound();
        db.EmployeeDocuments.Remove(d);
        await db.SaveChangesAsync();
        return Ok();
    }
}

// ---------------- Devices ----------------

[ApiController]
[Route("api/devices")]
[Authorize]
public class DevicesController(HrisDbContext db, AuditService audit) : ControllerBase
{
    private const string AdminRoles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)}";

    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await db.BiometricDevices.Include(d => d.Site).ThenInclude(s => s!.Branch)
            .Select(d => new
            {
                d.Id, d.SerialNumber, d.Name, d.Model, d.IpAddress, d.Port,
                status = d.Status.ToString(), d.LastSeenAt, d.FirmwareVersion,
                d.UserCount, d.FaceCount, d.FingerprintCount, d.LogCount, d.IsActive,
                site = d.Site == null ? null : new { d.Site.Id, d.Site.Name, branch = d.Site.Branch!.Name }
            }).ToListAsync());

    [HttpPost]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> Register(BiometricDevice d)
    {
        if (await db.BiometricDevices.AnyAsync(x => x.SerialNumber == d.SerialNumber))
            return BadRequest(new { message = "A device with this serial number is already registered." });
        d.Site = null;
        d.Status = DeviceStatus.Offline;
        db.BiometricDevices.Add(d);
        audit.Log(AuditCategory.Device, $"Registered device {d.SerialNumber}", nameof(BiometricDevice));
        await db.SaveChangesAsync();
        return Ok(d);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> Update(int id, BiometricDevice input)
    {
        var d = await db.BiometricDevices.FindAsync(id);
        if (d is null) return NotFound();
        d.Name = input.Name; d.Model = input.Model; d.SiteId = input.SiteId;
        d.IpAddress = input.IpAddress; d.Port = input.Port; d.IsActive = input.IsActive;
        audit.Log(AuditCategory.Device, $"Updated device {d.SerialNumber}", nameof(BiometricDevice), id.ToString());
        await db.SaveChangesAsync();
        return Ok(d);
    }

    [HttpGet("{id:int}/activity")]
    public async Task<IActionResult> Activity(int id) =>
        Ok(await db.DeviceActivityLogs.Where(l => l.DeviceId == id)
            .OrderByDescending(l => l.CreatedAt).Take(100).ToListAsync());
}
