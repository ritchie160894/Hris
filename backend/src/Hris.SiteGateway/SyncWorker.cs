using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hris.SiteGateway;

/// <summary>
/// Background synchronization service. Runs forever:
///  1. Heartbeat to the central server (site + device health).
///  2. Push queued attendance (retry with exponential backoff; never loses records while offline).
///  3. Pull employee + biometric template updates into the local cache.
/// Each step is isolated — a failed heartbeat does not block attendance upload when central is reachable.
/// </summary>
public class SyncWorker(IServiceScopeFactory scopeFactory, IHttpClientFactory httpFactory, IConfiguration config, ILogger<SyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(config.GetValue("Sync:IntervalSeconds", 60));
        logger.LogInformation("Sync worker started. Interval: {Interval}", interval);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
                var http = httpFactory.CreateClient("central");

                var centralReachable = false;

                centralReachable |= await TryStepAsync("heartbeat", () => SendHeartbeatAsync(db, http, ct));
                centralReachable |= await TryStepAsync("attendance push", () => PushAttendanceAsync(db, http, ct));
                centralReachable |= await TryStepAsync("enrollment pull", () => PullEnrollmentsAsync(db, http, ct));
                centralReachable |= await TryStepAsync("biometric template push", () => PushBiometricTemplatesAsync(db, http, ct));
                centralReachable |= await TryStepAsync("employee pull", () => PullEmployeesAsync(db, http, ct));

                await SetGatewayStateAsync(db, "centralOnline", centralReachable ? "true" : "false", ct);
                if (centralReachable)
                    await SetGatewayStateAsync(db, "lastSuccessfulSync", DateTime.UtcNow.ToString("O"), ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sync cycle failed unexpectedly.");
            }
            await Task.Delay(interval, ct);
        }
    }

    private async Task<bool> TryStepAsync(string stepName, Func<Task> step)
    {
        try
        {
            await step();
            return true;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Sync step '{Step}' — central unreachable ({Message}). Site keeps collecting offline.", stepName, ex.Message);
            return false;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning("Sync step '{Step}' timed out ({Message}). Will retry.", stepName, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sync step '{Step}' failed.", stepName);
            return false;
        }
    }

    private static async Task SetGatewayStateAsync(LocalDbContext db, string key, string value, CancellationToken ct)
    {
        var state = await db.State.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (state is null)
        {
            state = new GatewayState { Key = key };
            db.State.Add(state);
        }
        state.Value = value;
        await db.SaveChangesAsync(ct);
    }

    private async Task SendHeartbeatAsync(LocalDbContext db, HttpClient http, CancellationToken ct)
    {
        var pending = await db.Attendance.CountAsync(a => !a.Synced && !a.PermanentFailure, ct);
        var failed = await db.Attendance.CountAsync(a => a.PermanentFailure, ct);
        var devices = await db.Devices.Select(d => new
        {
            serialNumber = d.SerialNumber,
            online = d.LastSeenAt != null && d.LastSeenAt > DateTime.UtcNow.AddMinutes(-5),
            firmwareVersion = d.FirmwareVersion,
            userCount = d.UserCount, faceCount = d.FaceCount, fingerprintCount = d.FingerprintCount, logCount = d.LogCount
        }).ToListAsync(ct);

        var resp = await http.PostAsJsonAsync("api/sync/heartbeat", new { pendingCount = pending, failedCount = failed, devices }, ct);
        resp.EnsureSuccessStatusCode();
    }

    private int MaxRetryAttempts => config.GetValue("Sync:MaxRetryAttempts", 50);
    private int BackoffBaseSeconds => config.GetValue("Sync:BackoffBaseSeconds", 30);
    private int BatchSize => config.GetValue("Sync:PushBatchSize", 500);

    private DateTime ComputeNextRetry(int syncAttempts) =>
        DateTime.UtcNow.AddSeconds(Math.Min(3600, BackoffBaseSeconds * Math.Pow(2, Math.Min(syncAttempts, 8))));

    private async Task PushAttendanceAsync(LocalDbContext db, HttpClient http, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        while (!ct.IsCancellationRequested)
        {
            var batch = await db.Attendance
                .Where(a => !a.Synced && !a.PermanentFailure)
                .Where(a => a.NextRetryAt == null || a.NextRetryAt <= now)
                .OrderBy(a => a.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) return;

            PushResult? result = null;
            try
            {
                var payload = new
                {
                    records = batch.Select(a => new
                    {
                        syncGuid = a.SyncGuid,
                        biometricUserId = a.BiometricUserId,
                        punchTime = a.PunchTime,
                        punchType = a.PunchType,
                        verifyMode = a.VerifyMode,
                        deviceSerial = a.DeviceSerial
                    })
                };

                var resp = await http.PostAsJsonAsync("api/sync/attendance", payload, ct);
                resp.EnsureSuccessStatusCode();
                result = await resp.Content.ReadFromJsonAsync<PushResult>(cancellationToken: ct)
                         ?? new PushResult([], [], []);
            }
            catch (HttpRequestException ex)
            {
                foreach (var rec in batch)
                {
                    rec.SyncAttempts++;
                    rec.LastSyncError = ex.Message;
                    rec.NextRetryAt = ComputeNextRetry(rec.SyncAttempts);
                    if (rec.SyncAttempts >= MaxRetryAttempts)
                    {
                        rec.PermanentFailure = true;
                        rec.LastSyncError = $"Max retry attempts ({MaxRetryAttempts}) exceeded: {ex.Message}";
                    }
                }
                await db.SaveChangesAsync(ct);
                throw;
            }

            var resolved = result.Accepted.Concat(result.Duplicates).ToHashSet();
            var failedGuids = ParseFailedGuids(result.Failed);

            foreach (var rec in batch)
            {
                rec.SyncAttempts++;
                if (resolved.Contains(rec.SyncGuid))
                {
                    rec.Synced = true;
                    rec.SyncedAt = DateTime.UtcNow;
                    rec.LastSyncError = null;
                    rec.NextRetryAt = null;
                    continue;
                }

                if (failedGuids.TryGetValue(rec.SyncGuid, out var reason))
                {
                    rec.LastSyncError = reason;
                    if (reason.Contains("Unknown biometric user", StringComparison.OrdinalIgnoreCase))
                    {
                        rec.PermanentFailure = true;
                    }
                    else
                    {
                        rec.NextRetryAt = ComputeNextRetry(rec.SyncAttempts);
                        if (rec.SyncAttempts >= MaxRetryAttempts)
                            rec.PermanentFailure = true;
                    }
                }
                else
                {
                    rec.LastSyncError = "Not accepted by central server";
                    rec.NextRetryAt = ComputeNextRetry(rec.SyncAttempts);
                }
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Pushed {Count} attendance record(s): {Accepted} accepted, {Dup} duplicates, {Failed} failed.",
                batch.Count, result.Accepted.Count, result.Duplicates.Count, result.Failed.Count);

            if (batch.Count < BatchSize) return;
        }
    }

    private static Dictionary<Guid, string> ParseFailedGuids(List<object> failed)
    {
        var map = new Dictionary<Guid, string>();
        foreach (var item in failed)
        {
            try
            {
                var json = JsonSerializer.Serialize(item);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("syncGuid", out var g) && g.TryGetGuid(out var guid))
                {
                    var reason = doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString() ?? "failed" : "failed";
                    map[guid] = reason;
                }
            }
            catch { /* ignore malformed entries */ }
        }
        return map;
    }

    private record PushResult(List<Guid> Accepted, List<Guid> Duplicates, List<object> Failed);

    private async Task PullEmployeesAsync(LocalDbContext db, HttpClient http, CancellationToken ct)
    {
        var stateKey = await db.State.FirstOrDefaultAsync(s => s.Key == "lastEmployeePull", ct);
        var since = stateKey?.Value;
        var url = "api/sync/employees" + (string.IsNullOrEmpty(since) ? "" : $"?updatedSince={Uri.EscapeDataString(since)}");

        var result = await http.GetFromJsonAsync<EmployeePullResult>(url, ct);
        if (result is null) return;

        foreach (var emp in result.Employees)
        {
            var bioId = string.IsNullOrEmpty(emp.BiometricUserId) ? emp.EmployeeCode : emp.BiometricUserId;
            var local = await db.Employees.FirstOrDefaultAsync(e => e.BiometricUserId == bioId, ct);
            if (local is null)
            {
                local = new LocalEmployee { BiometricUserId = bioId };
                db.Employees.Add(local);
            }
            local.CentralId = emp.Id;
            local.EmployeeCode = emp.EmployeeCode;
            local.Name = emp.Name;
            local.UpdatedAt = DateTime.UtcNow;

            foreach (var t in emp.Templates)
            {
                var localT = await db.Templates.FirstOrDefaultAsync(x => x.CentralTemplateId == t.Id, ct);
                if (localT is null)
                {
                    localT = new LocalTemplate { CentralTemplateId = t.Id };
                    db.Templates.Add(localT);
                }
                else if (localT.Version == t.Version) continue;
                localT.BiometricUserId = bioId;
                localT.Type = t.Type;
                localT.FingerIndex = t.FingerIndex;
                localT.TemplateData = t.TemplateData;
                localT.Version = t.Version;
                localT.PushedToDevices = false;
            }
        }

        if (result.Employees.Count > 0)
            logger.LogInformation("Pulled {Count} employee update(s) from central.", result.Employees.Count);

        if (stateKey is null)
        {
            stateKey = new GatewayState { Key = "lastEmployeePull" };
            db.State.Add(stateKey);
        }
        stateKey.Value = result.ServerTime.ToString("O");
        await db.SaveChangesAsync(ct);

        await QueueTemplatePushAsync(db, ct);
    }

    private record EmployeePullResult(DateTime ServerTime, List<EmployeeDto> Employees);
    private record EmployeeDto(int Id, string EmployeeCode, string? BiometricUserId, string Name, int? SiteId, List<TemplateDto> Templates);
    private record TemplateDto(int Id, int Type, int FingerIndex, string TemplateData, int Version, DateTime CapturedAt);

    private async Task QueueTemplatePushAsync(LocalDbContext db, CancellationToken ct)
    {
        var pendingTemplates = await db.Templates.Where(t => !t.PushedToDevices).Take(50).ToListAsync(ct);
        if (pendingTemplates.Count == 0) return;

        var devices = await db.Devices.ToListAsync(ct);
        if (devices.Count == 0) return;

        foreach (var device in devices)
        {
            var commands = new List<string>();
            var cmdId = (int)(DateTime.UtcNow.Ticks % 100000);
            foreach (var t in pendingTemplates)
            {
                var emp = await db.Employees.FirstOrDefaultAsync(e => e.BiometricUserId == t.BiometricUserId, ct);
                if (emp is null) continue;
                commands.Add($"C:{cmdId++}:DATA UPDATE USERINFO PIN={emp.BiometricUserId}\tName={emp.Name}\tPri=0");
                commands.Add(t.Type == 1
                    ? $"C:{cmdId++}:DATA UPDATE BIODATA Pin={t.BiometricUserId}\tNo=0\tIndex=0\tValid=1\tDuress=0\tType=9\tMajorVer=5\tMinorVer=8\tFormat=0\tTmp={t.TemplateData}"
                    : $"C:{cmdId++}:DATA UPDATE FINGERTMP PIN={t.BiometricUserId}\tFID={t.FingerIndex}\tSize={t.TemplateData.Length}\tValid=1\tTMP={t.TemplateData}");
            }
            device.PendingCommands = string.Join("\r\n", commands);
        }

        foreach (var t in pendingTemplates) t.PushedToDevices = true;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Queued {Count} biometric template update(s) for {Devices} device(s).", pendingTemplates.Count, devices.Count);
    }

    private record PendingEnrollmentResult(DateTime ServerTime, List<PendingEnrollmentDto> Enrollments);
    private record PendingEnrollmentDto(int Id, string DeviceSerial, string BiometricUserId, string EmployeeName, int Type, int FingerIndex, string? DeviceCommand);

    private async Task PullEnrollmentsAsync(LocalDbContext db, HttpClient http, CancellationToken ct)
    {
        PendingEnrollmentResult? result;
        try
        {
            result = await http.GetFromJsonAsync<PendingEnrollmentResult>("api/sync/enrollments/pending", ct);
        }
        catch
        {
            return;
        }
        if (result?.Enrollments is null || result.Enrollments.Count == 0) return;

        var dispatched = new List<int>();
        foreach (var en in result.Enrollments)
        {
            var device = await db.Devices.FirstOrDefaultAsync(d => d.SerialNumber == en.DeviceSerial, ct);
            if (device is null) continue;

            if (!string.IsNullOrEmpty(en.DeviceCommand))
            {
                device.PendingCommands = string.IsNullOrEmpty(device.PendingCommands)
                    ? en.DeviceCommand
                    : device.PendingCommands + "\r\n" + en.DeviceCommand;
            }
            dispatched.Add(en.Id);
        }

        if (dispatched.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            await http.PostAsJsonAsync("api/sync/enrollments/dispatched", new { enrollmentIds = dispatched }, ct);
            logger.LogInformation("Queued {Count} enrollment command(s) for SenseFace device(s).", dispatched.Count);
        }
    }

    private async Task PushBiometricTemplatesAsync(LocalDbContext db, HttpClient http, CancellationToken ct)
    {
        const int batchSize = 20;
        var now = DateTime.UtcNow;
        var batch = await db.TemplateUploads
            .Where(t => !t.Synced && (t.NextRetryAt == null || t.NextRetryAt <= now))
            .OrderBy(t => t.Id).Take(batchSize).ToListAsync(ct);
        if (batch.Count == 0) return;

        foreach (var t in batch)
        {
            try
            {
                var resp = await http.PostAsJsonAsync("api/sync/biometric-templates", new
                {
                    biometricUserId = t.BiometricUserId,
                    type = t.Type,
                    fingerIndex = t.FingerIndex,
                    templateData = t.TemplateData,
                    deviceSerial = t.DeviceSerial,
                    enrollmentId = t.EnrollmentId
                }, ct);

                if (resp.IsSuccessStatusCode)
                {
                    t.Synced = true;
                    t.LastSyncError = null;
                }
                else
                {
                    t.SyncAttempts++;
                    t.LastSyncError = $"HTTP {(int)resp.StatusCode}";
                    t.NextRetryAt = ComputeNextRetry(t.SyncAttempts);
                    logger.LogWarning("Failed to upload biometric template for {Pin} (HTTP {Code}).", t.BiometricUserId, resp.StatusCode);
                }
            }
            catch (HttpRequestException ex)
            {
                t.SyncAttempts++;
                t.LastSyncError = ex.Message;
                t.NextRetryAt = ComputeNextRetry(t.SyncAttempts);
                throw;
            }
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Uploaded {Count} biometric template(s) to central.", batch.Count(t => t.Synced));
    }
}
