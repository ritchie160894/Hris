using Hris.Api.Data;
using Hris.Api.Services;
using Hris.Api.Services.Biometric;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Controllers;

/// <summary>
/// Endpoints used by branch/site gateway services. Authenticated with the per-site
/// gateway API key (X-Site-Key header) instead of JWT, since gateways are headless services.
/// </summary>
[ApiController]
[Route("api/sync")]
public class SyncController(HrisDbContext db, AttendanceSummaryService summaries, DashboardService dashboard, BiometricEnrollmentService biometric, SyncBatchMaintenanceService batchMaintenance, AuditService audit) : ControllerBase
{
    private async Task<Site?> AuthenticateSiteAsync()
    {
        if (!Request.Headers.TryGetValue("X-Site-Key", out var key)) return null;
        return await db.Sites.FirstOrDefaultAsync(s => s.GatewayApiKey == key.ToString() && s.IsActive);
    }

    public record HeartbeatRequest(int PendingCount, int? FailedCount, List<DeviceHeartbeat>? Devices);
    public record DeviceHeartbeat(string SerialNumber, bool Online, string? FirmwareVersion, int UserCount, int FaceCount, int FingerprintCount, int LogCount);

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat(HeartbeatRequest req)
    {
        var site = await AuthenticateSiteAsync();
        if (site is null) return Unauthorized();

        site.LastHeartbeatAt = DateTime.UtcNow;
        site.PendingSyncCount = req.PendingCount;

        if (req.Devices is not null)
        {
            foreach (var dh in req.Devices)
            {
                var device = await db.BiometricDevices.FirstOrDefaultAsync(d => d.SerialNumber == dh.SerialNumber);
                if (device is null)
                {
                    device = new BiometricDevice { SerialNumber = dh.SerialNumber, Name = dh.SerialNumber, SiteId = site.Id };
                    db.BiometricDevices.Add(device);
                }
                device.Status = dh.Online ? DeviceStatus.Online : DeviceStatus.Offline;
                device.LastSeenAt = dh.Online ? DateTime.UtcNow : device.LastSeenAt;
                device.FirmwareVersion = dh.FirmwareVersion ?? device.FirmwareVersion;
                device.UserCount = dh.UserCount;
                device.FaceCount = dh.FaceCount;
                device.FingerprintCount = dh.FingerprintCount;
                device.LogCount = dh.LogCount;
            }
        }
        await db.SaveChangesAsync();
        return Ok(new { serverTime = DateTime.UtcNow });
    }

    public record AttendancePush(List<AttendanceRecord> Records);
    public record AttendanceRecord(Guid SyncGuid, string BiometricUserId, DateTime PunchTime, int PunchType, string? VerifyMode, string? DeviceSerial);

    /// <summary>Receives queued attendance from a site. Idempotent: duplicates are detected and skipped.</summary>
    [HttpPost("attendance")]
    public async Task<IActionResult> PushAttendance(AttendancePush push)
    {
        var site = await AuthenticateSiteAsync();
        if (site is null) return Unauthorized();

        var batch = new SyncBatch
        {
            SiteId = site.Id, Direction = SyncDirection.SiteToCentral, DataType = "Attendance",
            RecordCount = push.Records.Count, Status = SyncStatus.InProgress
        };
        db.SyncBatches.Add(batch);

        var accepted = new List<Guid>();
        var duplicates = new List<Guid>();
        var failed = new List<object>();
        var summaryDates = new HashSet<(int EmployeeId, DateOnly Date)>();

        var guids = push.Records.Select(r => r.SyncGuid).ToList();
        var existingGuids = await db.AttendanceLogs.Where(a => guids.Contains(a.SyncGuid)).Select(a => a.SyncGuid).ToListAsync();

        var bioIds = push.Records.Select(r => r.BiometricUserId).Distinct().ToList();
        var employees = await db.Employees
            .Where(e => bioIds.Contains(e.BiometricUserId!) || bioIds.Contains(e.EmployeeCode))
            .ToDictionaryAsync(e => e.BiometricUserId ?? e.EmployeeCode);

        var execIds = await ExecutiveExemption.GetExemptEmployeeIdsAsync(db);
        var devices = await db.BiometricDevices.Where(d => d.SiteId == site.Id).ToListAsync();

        foreach (var rec in push.Records)
        {
            if (existingGuids.Contains(rec.SyncGuid)) { duplicates.Add(rec.SyncGuid); continue; }

            if (!employees.TryGetValue(rec.BiometricUserId, out var emp))
            {
                failed.Add(new { rec.SyncGuid, reason = $"Unknown biometric user id '{rec.BiometricUserId}'" });
                db.SyncConflicts.Add(new SyncConflict
                {
                    SiteId = site.Id, DataType = "Attendance",
                    Description = $"Punch from unknown biometric user '{rec.BiometricUserId}' at {rec.PunchTime:yyyy-MM-dd HH:mm}",
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(rec)
                });
                continue;
            }

            if (execIds.Contains(emp.Id))
            {
                duplicates.Add(rec.SyncGuid);
                continue;
            }

            // duplicate by natural key (same employee/time/type from a different sync guid)
            var dup = await db.AttendanceLogs.AnyAsync(a =>
                a.EmployeeId == emp.Id && a.PunchTime == rec.PunchTime && a.PunchType == (PunchType)rec.PunchType);
            if (dup) { duplicates.Add(rec.SyncGuid); continue; }

            var device = devices.FirstOrDefault(d => d.SerialNumber == rec.DeviceSerial);
            var log = new AttendanceLog
            {
                SyncGuid = rec.SyncGuid,
                EmployeeId = emp.Id,
                SiteId = site.Id,
                DeviceId = device?.Id,
                PunchTime = rec.PunchTime,
                PunchType = (PunchType)rec.PunchType,
                Source = AttendanceSource.Biometric,
                VerifyMode = rec.VerifyMode,
                SyncedAt = DateTime.UtcNow
            };
            AttendanceSummaryService.StampLog(log);
            db.AttendanceLogs.Add(log);
            accepted.Add(rec.SyncGuid);
            existingGuids.Add(rec.SyncGuid);
            summaryDates.Add((emp.Id, log.AttendanceDate));
        }

        batch.Status = SyncStatus.Completed;
        batch.DuplicateCount = duplicates.Count;
        batch.ConflictCount = failed.Count;
        batch.CompletedAt = DateTime.UtcNow;
        site.LastSyncAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        foreach (var (empId, day) in summaryDates)
            await summaries.UpsertDailyAsync(empId, day);
        await db.SaveChangesAsync();
        dashboard.Invalidate();

        // Accepted + duplicates are both safe for the gateway to mark as synced.
        return Ok(new { accepted, duplicates, failed });
    }

    /// <summary>Employee master data + biometric templates for the site cache (delta by updatedSince).</summary>
    [HttpGet("employees")]
    public async Task<IActionResult> PullEmployees([FromQuery] DateTime? updatedSince)
    {
        var site = await AuthenticateSiteAsync();
        if (site is null) return Unauthorized();

        var q = db.Employees
            .Include(e => e.BiometricTemplates)
            .Where(e => e.Status != EmploymentStatus.Resigned && e.Status != EmploymentStatus.Terminated && e.Status != EmploymentStatus.Retired);
        if (updatedSince.HasValue)
            q = q.Where(e => e.CreatedAt > updatedSince || (e.UpdatedAt != null && e.UpdatedAt > updatedSince)
                || e.BiometricTemplates.Any(t => t.CapturedAt > updatedSince));

        var employees = await q.Select(e => new
        {
            e.Id, e.EmployeeCode, e.BiometricUserId,
            name = e.FirstName + " " + e.LastName,
            e.SiteId,
            templates = e.BiometricTemplates.Select(t => new
            {
                t.Id, type = (int)t.Type, t.FingerIndex, t.TemplateData, t.Version, t.CapturedAt
            })
        }).ToListAsync();

        db.SyncBatches.Add(new SyncBatch
        {
            SiteId = site.Id, Direction = SyncDirection.CentralToSite, DataType = "Employee",
            RecordCount = employees.Count, Status = SyncStatus.Completed, CompletedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return Ok(new { serverTime = DateTime.UtcNow, employees });
    }

    /// <summary>Pending enrollment commands for site gateways to push to SenseFace devices.</summary>
    [HttpGet("enrollments/pending")]
    public async Task<IActionResult> PendingEnrollments()
    {
        var site = await AuthenticateSiteAsync();
        if (site is null) return Unauthorized();

        await biometric.ExpireStaleAsync();

        var deviceIds = await db.BiometricDevices.Where(d => d.SiteId == site.Id && d.IsActive).Select(d => d.Id).ToListAsync();
        var items = await db.BiometricEnrollments
            .Include(e => e.Employee).Include(e => e.Device)
            .Where(e => deviceIds.Contains(e.DeviceId)
                && !e.DispatchedToGateway
                && (e.Status == BiometricEnrollmentStatus.Pending || e.Status == BiometricEnrollmentStatus.WaitingOnDevice)
                && e.ExpiresAt > DateTime.UtcNow)
            .Select(e => new
            {
                e.Id,
                deviceSerial = e.Device!.SerialNumber,
                biometricUserId = e.Employee!.BiometricUserId ?? e.Employee.EmployeeCode,
                employeeName = e.Employee.FirstName + " " + e.Employee.LastName,
                type = (int)e.Type,
                e.FingerIndex,
                e.DeviceCommand
            }).ToListAsync();

        return Ok(new { serverTime = DateTime.UtcNow, enrollments = items });
    }

    public record DispatchedEnrollmentsRequest(List<int> EnrollmentIds);

    [HttpPost("enrollments/dispatched")]
    public async Task<IActionResult> MarkEnrollmentsDispatched(DispatchedEnrollmentsRequest req)
    {
        var site = await AuthenticateSiteAsync();
        if (site is null) return Unauthorized();

        var deviceIds = await db.BiometricDevices.Where(d => d.SiteId == site.Id).Select(d => d.Id).ToListAsync();
        var enrollments = await db.BiometricEnrollments
            .Where(e => req.EnrollmentIds.Contains(e.Id) && deviceIds.Contains(e.DeviceId))
            .ToListAsync();

        foreach (var e in enrollments)
        {
            e.DispatchedToGateway = true;
            if (e.Status == BiometricEnrollmentStatus.Pending)
            {
                e.Status = BiometricEnrollmentStatus.WaitingOnDevice;
                e.StartedAt = DateTime.UtcNow;
            }
        }
        await db.SaveChangesAsync();
        return Ok(new { marked = enrollments.Count });
    }

    public record BiometricTemplatePush(string BiometricUserId, int Type, int FingerIndex, string TemplateData, string? DeviceSerial, int? EnrollmentId);

    [HttpPost("biometric-templates")]
    public async Task<IActionResult> PushBiometricTemplate(BiometricTemplatePush push)
    {
        var site = await AuthenticateSiteAsync();
        if (site is null) return Unauthorized();

        try
        {
            var template = await biometric.SaveTemplateFromDeviceAsync(
                push.BiometricUserId,
                (BiometricTemplateType)push.Type,
                push.FingerIndex,
                push.TemplateData,
                push.DeviceSerial,
                push.EnrollmentId);

            db.SyncBatches.Add(new SyncBatch
            {
                SiteId = site.Id, Direction = SyncDirection.SiteToCentral, DataType = "BiometricTemplate",
                RecordCount = 1, Status = SyncStatus.Completed, CompletedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            return Ok(new { templateId = template.Id, version = template.Version });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ---- Monitoring endpoints for the web UI (JWT auth) ----

    [Authorize]
    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var sites = await db.Sites.Include(s => s.Branch).Where(s => s.IsActive)
            .Select(s => new
            {
                s.Id, s.Code, s.Name, branch = s.Branch!.Name,
                s.LastHeartbeatAt, s.LastSyncAt, s.PendingSyncCount,
                online = s.LastHeartbeatAt != null && s.LastHeartbeatAt > DateTime.UtcNow.AddMinutes(-10)
            }).ToListAsync();
        var batchTotal = await db.SyncBatches.CountAsync();
        var unresolvedConflicts = await db.SyncConflicts.Include(c => c.Site)
            .Where(c => c.Resolution == ConflictResolution.Unresolved)
            .OrderByDescending(c => c.CreatedAt).Take(100)
            .Select(c => new { c.Id, site = c.Site!.Name, c.DataType, c.Description, c.CreatedAt })
            .ToListAsync();
        return Ok(new { sites, batchTotal, unresolvedConflicts });
    }

    [Authorize]
    [HttpGet("batches")]
    public async Task<IActionResult> Batches([FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = db.SyncBatches.AsQueryable();
        var total = await q.CountAsync();
        var items = await q.Include(b => b.Site)
            .OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(b => new
            {
                b.Id, site = b.Site!.Name, direction = b.Direction.ToString(), b.DataType,
                b.RecordCount, b.DuplicateCount, b.ConflictCount, status = b.Status.ToString(),
                b.ErrorMessage, b.CreatedAt, b.CompletedAt
            }).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)}")]
    [HttpGet("settings")]
    public async Task<IActionResult> Settings()
    {
        var retentionDays = await batchMaintenance.GetRetentionDaysAsync();
        return Ok(new
        {
            batchRetentionDays = retentionDays,
            batchRetentionLabel = SyncBatchMaintenanceService.RetentionLabel(retentionDays),
            options = SyncBatchMaintenanceService.AllowedRetentionDays.Select(d => new
            {
                days = d,
                label = SyncBatchMaintenanceService.RetentionLabel(d)
            })
        });
    }

    public record UpdateSettingsRequest(int BatchRetentionDays);

    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)}")]
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(UpdateSettingsRequest req)
    {
        try
        {
            await batchMaintenance.SetRetentionDaysAsync(req.BatchRetentionDays);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var purged = await batchMaintenance.PurgeExpiredBatchesAsync();
        audit.Log(AuditCategory.Sync, $"Sync batch retention set to {SyncBatchMaintenanceService.RetentionLabel(req.BatchRetentionDays)}",
            "SyncBatch", null, purged > 0 ? $"Immediately purged {purged} old batch(es)." : null);

        return Ok(new
        {
            message = $"Batch retention updated to {SyncBatchMaintenanceService.RetentionLabel(req.BatchRetentionDays)}.",
            batchRetentionDays = req.BatchRetentionDays,
            purged
        });
    }

    public record ResolveRequest(string Resolution);

    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)}")]
    [HttpPost("conflicts/{id:int}/resolve")]
    public async Task<IActionResult> ResolveConflict(int id, ResolveRequest req)
    {
        var c = await db.SyncConflicts.FindAsync(id);
        if (c is null) return NotFound();
        if (Enum.TryParse<ConflictResolution>(req.Resolution, out var res)) c.Resolution = res;
        else c.Resolution = ConflictResolution.Discarded;
        c.ResolvedByName = User.DisplayName();
        c.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok();
    }
}
