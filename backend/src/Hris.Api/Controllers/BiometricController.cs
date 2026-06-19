using Hris.Api.Data;
using Hris.Api.Services.Biometric;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Controllers;

[ApiController]
[Route("api/biometric")]
[Authorize]
public class BiometricController(
    HrisDbContext db,
    BiometricEnrollmentService enrollmentService) : ControllerBase
{
    [HttpGet("devices")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)}")]
    public async Task<IActionResult> Devices([FromQuery] int? siteId)
    {
        var q = db.BiometricDevices.Include(d => d.Site).Where(d => d.IsActive);
        if (siteId.HasValue) q = q.Where(d => d.SiteId == siteId);
        var items = await q.OrderBy(d => d.Name).Select(d => new
        {
            d.Id, d.SerialNumber, d.Name, d.Model, d.IpAddress, d.Port,
            site = d.Site!.Name, siteId = d.SiteId,
            status = d.Status.ToString(), d.LastSeenAt,
            d.UserCount, d.FaceCount, d.FingerprintCount,
            online = d.LastSeenAt != null && d.LastSeenAt > DateTime.UtcNow.AddMinutes(-5)
        }).ToListAsync();
        return Ok(items);
    }

    [HttpGet("enrollments")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)}")]
    public async Task<IActionResult> ListEnrollments([FromQuery] int? employeeId, [FromQuery] int take = 20)
    {
        await enrollmentService.ExpireStaleAsync();
        var q = db.BiometricEnrollments
            .Include(e => e.Employee).Include(e => e.Device)
            .AsQueryable();
        if (employeeId.HasValue) q = q.Where(e => e.EmployeeId == employeeId);
        var items = await q.OrderByDescending(e => e.CreatedAt).Take(take)
            .Select(e => new
            {
                e.Id, e.EmployeeId,
                employee = e.Employee!.FirstName + " " + e.Employee.LastName,
                employeeCode = e.Employee.EmployeeCode,
                device = e.Device!.Name,
                deviceSerial = e.Device.SerialNumber,
                type = e.Type.ToString(),
                e.FingerIndex,
                status = e.Status.ToString(),
                e.RequestedByUserName,
                e.StartedAt, e.CompletedAt, e.ExpiresAt,
                e.ErrorMessage, e.ResultTemplateId,
                e.CreatedAt
            }).ToListAsync();
        return Ok(items);
    }

    [HttpGet("enrollments/{id:int}")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)}")]
    public async Task<IActionResult> GetEnrollment(int id)
    {
        var e = await db.BiometricEnrollments
            .Include(x => x.Employee).Include(x => x.Device)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return Ok(new
        {
            e.Id, e.EmployeeId,
            employee = e.Employee!.FullName,
            e.Employee!.EmployeeCode,
            e.Employee!.BiometricUserId,
            device = e.Device!.Name,
            deviceSerial = e.Device!.SerialNumber,
            type = (int)e.Type,
            typeName = e.Type.ToString(),
            e.FingerIndex,
            status = (int)e.Status,
            statusName = e.Status.ToString(),
            e.RequestedByUserName,
            e.StartedAt, e.CompletedAt, e.ExpiresAt,
            e.ErrorMessage, e.ResultTemplateId,
            e.DispatchedToGateway,
            e.CreatedAt
        });
    }

    public record StartEnrollmentRequest(int EmployeeId, int DeviceId, int Type, int FingerIndex = 0);

    [HttpPost("enrollments")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)}")]
    public async Task<IActionResult> StartEnrollment(StartEnrollmentRequest req)
    {
        try
        {
            var enrollment = await enrollmentService.StartAsync(
                req.EmployeeId, req.DeviceId, (BiometricTemplateType)req.Type, req.FingerIndex, User.DisplayName());
            return Ok(new
            {
                enrollment.Id,
                status = enrollment.Status.ToString(),
                enrollment.ExpiresAt,
                message = "Enrollment started. Employee should scan at the selected SenseFace device."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("enrollments/{id:int}/cancel")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)}")]
    public async Task<IActionResult> CancelEnrollment(int id)
    {
        try
        {
            await enrollmentService.CancelAsync(id);
            return Ok(new { message = "Enrollment cancelled." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("templates/{id:int}")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)}")]
    public async Task<IActionResult> DeleteTemplate(int id)
    {
        var t = await db.BiometricTemplates.Include(x => x.Employee).FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();
        db.BiometricTemplates.Remove(t);
        if (t.Employee is not null) t.Employee.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { message = "Template removed. Re-enroll on device to restore." });
    }
}
