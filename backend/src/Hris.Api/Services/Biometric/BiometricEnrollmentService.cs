using Hris.Api.Data;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Services.Biometric;

public class BiometricEnrollmentService(
    HrisDbContext db,
    IServiceScopeFactory scopeFactory,
    ISenseFaceDeviceAdapter adapter,
    IConfiguration config,
    AuditService audit,
    ILogger<BiometricEnrollmentService> logger)
{
    public async Task<BiometricEnrollment> StartAsync(
        int employeeId, int deviceId, BiometricTemplateType type, int fingerIndex, string requestedBy, CancellationToken ct = default)
    {
        if (type == BiometricTemplateType.Fingerprint && fingerIndex is < 0 or > 9)
            throw new InvalidOperationException("Fingerprint index must be 0–9 (left thumb = 0, right thumb = 5).");

        var employee = await db.Employees.FindAsync([employeeId], ct)
                       ?? throw new InvalidOperationException("Employee not found.");

        var device = await db.BiometricDevices.Include(d => d.Site)
                           .FirstOrDefaultAsync(d => d.Id == deviceId && d.IsActive, ct)
                       ?? throw new InvalidOperationException("Device not found or inactive.");

        var provider = config["Biometric:Provider"] ?? "Gateway";
        var isSimulated = string.Equals(provider, "Simulated", StringComparison.OrdinalIgnoreCase);

        if (!isSimulated && !IsDeviceOnline(device))
            throw new InvalidOperationException(
                "Device is offline. Ensure the SenseFace device is powered on, connected to the network, and the site gateway is running before starting enrollment.");

        employee.BiometricUserId ??= employee.EmployeeCode;

        var active = await db.BiometricEnrollments.AnyAsync(e =>
            e.EmployeeId == employeeId &&
            (e.Status == BiometricEnrollmentStatus.Pending || e.Status == BiometricEnrollmentStatus.WaitingOnDevice), ct);
        if (active)
            throw new InvalidOperationException("This employee already has an enrollment in progress.");

        var timeoutMin = config.GetValue("Biometric:EnrollmentTimeoutMinutes", 15);
        var enrollment = new BiometricEnrollment
        {
            EmployeeId = employeeId,
            DeviceId = deviceId,
            Type = type,
            FingerIndex = type == BiometricTemplateType.Face ? 0 : fingerIndex,
            Status = BiometricEnrollmentStatus.Pending,
            RequestedByUserName = requestedBy,
            ExpiresAt = DateTime.UtcNow.AddMinutes(timeoutMin)
        };
        enrollment.DeviceCommand = adapter.BuildEnrollmentCommand(enrollment, employee, device);

        db.BiometricEnrollments.Add(enrollment);
        audit.Log(AuditCategory.Device,
            $"Started {type} enrollment for {employee.EmployeeCode} on device {device.SerialNumber}",
            nameof(BiometricEnrollment));
        await db.SaveChangesAsync(ct);

        if (isSimulated)
            _ = Task.Run(() => CompleteSimulatedAsync(enrollment.Id));
        else if (adapter.SupportsDirectEnrollment)
            _ = Task.Run(() => TryDirectEnrollmentAsync(enrollment.Id));

        return enrollment;
    }

    private static bool IsDeviceOnline(BiometricDevice device) =>
        device.Status == DeviceStatus.Online
        || (device.LastSeenAt.HasValue && device.LastSeenAt > DateTime.UtcNow.AddMinutes(-5));

    private async Task CompleteSimulatedAsync(int enrollmentId)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var scopedDb = scope.ServiceProvider.GetRequiredService<HrisDbContext>();
            var scopedAdapter = scope.ServiceProvider.GetRequiredService<ISenseFaceDeviceAdapter>();

            var enrollment = await scopedDb.BiometricEnrollments
                .Include(e => e.Employee).Include(e => e.Device)
                .FirstOrDefaultAsync(e => e.Id == enrollmentId);
            if (enrollment is null || enrollment.Status is BiometricEnrollmentStatus.Completed or BiometricEnrollmentStatus.Cancelled)
                return;

            enrollment.Status = BiometricEnrollmentStatus.WaitingOnDevice;
            enrollment.StartedAt = DateTime.UtcNow;
            await scopedDb.SaveChangesAsync();

            var result = await scopedAdapter.SimulateCaptureAsync(enrollment, enrollment.Employee!);
            if (!result.Success)
            {
                await FailAsync(enrollmentId, result.ErrorMessage ?? "Simulation failed.");
                return;
            }

            var svc = scope.ServiceProvider.GetRequiredService<BiometricEnrollmentService>();
            await svc.SaveTemplateFromDeviceAsync(
                enrollment.Employee!.BiometricUserId ?? enrollment.Employee.EmployeeCode,
                enrollment.Type, enrollment.FingerIndex, result.TemplateDataBase64!,
                enrollment.Device?.SerialNumber, enrollment.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Simulated enrollment {Id} failed.", enrollmentId);
            await FailAsync(enrollmentId, ex.Message);
        }
    }

    private async Task TryDirectEnrollmentAsync(int enrollmentId)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var scopedDb = scope.ServiceProvider.GetRequiredService<HrisDbContext>();
            var scopedAdapter = scope.ServiceProvider.GetRequiredService<ISenseFaceDeviceAdapter>();

            var enrollment = await scopedDb.BiometricEnrollments
                .Include(e => e.Employee).Include(e => e.Device)
                .FirstOrDefaultAsync(e => e.Id == enrollmentId);
            if (enrollment?.Employee is null || enrollment.Device is null) return;

            enrollment.Status = BiometricEnrollmentStatus.WaitingOnDevice;
            enrollment.StartedAt = DateTime.UtcNow;
            await scopedDb.SaveChangesAsync();

            var result = await scopedAdapter.StartDirectEnrollmentAsync(enrollment, enrollment.Employee, enrollment.Device);
            if (result.Success && !string.IsNullOrEmpty(result.TemplateDataBase64))
            {
                var svc = scope.ServiceProvider.GetRequiredService<BiometricEnrollmentService>();
                await svc.SaveTemplateFromDeviceAsync(
                    enrollment.Employee.BiometricUserId ?? enrollment.Employee.EmployeeCode,
                    enrollment.Type, enrollment.FingerIndex, result.TemplateDataBase64,
                    enrollment.Device.SerialNumber, enrollment.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Direct SDK enrollment {Id} failed.", enrollmentId);
            await FailAsync(enrollmentId, ex.Message);
        }
    }

    public async Task<BiometricTemplate> SaveTemplateFromDeviceAsync(
        string biometricUserId, BiometricTemplateType type, int fingerIndex, string templateDataBase64,
        string? deviceSerial, int? enrollmentId = null, CancellationToken ct = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e =>
            e.BiometricUserId == biometricUserId || e.EmployeeCode == biometricUserId, ct)
            ?? throw new InvalidOperationException($"Unknown biometric user '{biometricUserId}'.");

        var device = string.IsNullOrEmpty(deviceSerial)
            ? null
            : await db.BiometricDevices.FirstOrDefaultAsync(d => d.SerialNumber == deviceSerial, ct);

        var existing = await db.BiometricTemplates.FirstOrDefaultAsync(t =>
            t.EmployeeId == employee.Id && t.Type == type && t.FingerIndex == fingerIndex, ct);

        if (existing is null)
        {
            existing = new BiometricTemplate
            {
                EmployeeId = employee.Id,
                Type = type,
                FingerIndex = fingerIndex,
                TemplateData = templateDataBase64,
                Version = 1,
                CapturedAt = DateTime.UtcNow,
                DeviceId = device?.Id,
                CapturedOnDeviceSerial = deviceSerial
            };
            db.BiometricTemplates.Add(existing);
        }
        else
        {
            existing.TemplateData = templateDataBase64;
            existing.Version++;
            existing.CapturedAt = DateTime.UtcNow;
            existing.DeviceId = device?.Id;
            existing.CapturedOnDeviceSerial = deviceSerial;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        employee.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        if (enrollmentId.HasValue)
        {
            var enrollment = await db.BiometricEnrollments.FindAsync([enrollmentId.Value], ct);
            if (enrollment is not null)
            {
                enrollment.Status = BiometricEnrollmentStatus.Completed;
                enrollment.CompletedAt = DateTime.UtcNow;
                enrollment.ResultTemplateId = existing.Id;
            }
        }
        else
        {
            var open = await db.BiometricEnrollments
                .Where(e => e.EmployeeId == employee.Id && e.Type == type && e.FingerIndex == fingerIndex
                    && (e.Status == BiometricEnrollmentStatus.Pending || e.Status == BiometricEnrollmentStatus.WaitingOnDevice))
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (open is not null)
            {
                open.Status = BiometricEnrollmentStatus.Completed;
                open.CompletedAt = DateTime.UtcNow;
                open.ResultTemplateId = existing.Id;
            }
        }

        audit.Log(AuditCategory.Device,
            $"Captured {type} template for {employee.EmployeeCode} (finger {fingerIndex}) from device {deviceSerial ?? "unknown"}",
            nameof(BiometricTemplate), existing.Id.ToString());

        await db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task FailAsync(int enrollmentId, string message)
    {
        using var scope = scopeFactory.CreateScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<HrisDbContext>();
        var enrollment = await scopedDb.BiometricEnrollments.FindAsync(enrollmentId);
        if (enrollment is null) return;
        enrollment.Status = BiometricEnrollmentStatus.Failed;
        enrollment.ErrorMessage = message;
        enrollment.CompletedAt = DateTime.UtcNow;
        await scopedDb.SaveChangesAsync();
    }

    public async Task CancelAsync(int enrollmentId, CancellationToken ct = default)
    {
        var enrollment = await db.BiometricEnrollments.FindAsync([enrollmentId], ct)
                         ?? throw new InvalidOperationException("Enrollment not found.");
        if (enrollment.Status is BiometricEnrollmentStatus.Completed or BiometricEnrollmentStatus.Cancelled)
            throw new InvalidOperationException("Enrollment is already finished.");

        enrollment.Status = BiometricEnrollmentStatus.Cancelled;
        enrollment.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task ExpireStaleAsync(CancellationToken ct = default)
    {
        var stale = await db.BiometricEnrollments
            .Where(e => (e.Status == BiometricEnrollmentStatus.Pending || e.Status == BiometricEnrollmentStatus.WaitingOnDevice)
                        && e.ExpiresAt < DateTime.UtcNow)
            .ToListAsync(ct);
        foreach (var e in stale)
        {
            e.Status = BiometricEnrollmentStatus.Expired;
            e.CompletedAt = DateTime.UtcNow;
            e.ErrorMessage = "Enrollment session timed out.";
        }
        if (stale.Count > 0) await db.SaveChangesAsync(ct);
    }
}
