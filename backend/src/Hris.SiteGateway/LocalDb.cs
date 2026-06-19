using Microsoft.EntityFrameworkCore;

namespace Hris.SiteGateway;

/// <summary>
/// Local site database. Stores the employee cache, queued attendance punches and
/// device state so the site keeps operating with no internet connectivity.
/// Uses SQL Server Express when configured ("Provider": "SqlServer"), otherwise SQLite.
/// </summary>
public class LocalDbContext(DbContextOptions<LocalDbContext> options) : DbContext(options)
{
    public DbSet<LocalEmployee> Employees => Set<LocalEmployee>();
    public DbSet<LocalTemplate> Templates => Set<LocalTemplate>();
    public DbSet<LocalAttendance> Attendance => Set<LocalAttendance>();
    public DbSet<LocalDevice> Devices => Set<LocalDevice>();
    public DbSet<LocalTemplateUpload> TemplateUploads => Set<LocalTemplateUpload>();
    public DbSet<GatewayState> State => Set<GatewayState>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<LocalEmployee>().HasIndex(e => e.BiometricUserId).IsUnique();
        b.Entity<LocalAttendance>().HasIndex(a => a.SyncGuid).IsUnique();
        b.Entity<LocalAttendance>().HasIndex(a => new { a.BiometricUserId, a.PunchTime, a.PunchType }).IsUnique();
        b.Entity<LocalAttendance>().HasIndex(a => a.Synced);
        b.Entity<LocalAttendance>().HasIndex(a => new { a.Synced, a.NextRetryAt });
        b.Entity<LocalDevice>().HasIndex(d => d.SerialNumber).IsUnique();
        b.Entity<LocalTemplateUpload>().HasIndex(t => t.Synced);
    }
}

/// <summary>Employee cache synchronized from the central server (used for local verification).</summary>
public class LocalEmployee
{
    public int Id { get; set; }
    public int CentralId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string BiometricUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public class LocalTemplate
{
    public int Id { get; set; }
    public int CentralTemplateId { get; set; }
    public string BiometricUserId { get; set; } = string.Empty;
    public int Type { get; set; } // 1=face 2=fingerprint
    public int FingerIndex { get; set; }
    public string TemplateData { get; set; } = string.Empty;
    public int Version { get; set; }
    public bool PushedToDevices { get; set; }
}

/// <summary>Attendance punch queued for synchronization to the central server.</summary>
public class LocalAttendance
{
    public int Id { get; set; }
    public Guid SyncGuid { get; set; } = Guid.NewGuid();
    public string BiometricUserId { get; set; } = string.Empty;
    public DateTime PunchTime { get; set; }
    public int PunchType { get; set; } // 1=in 2=out 3=break-in 4=break-out
    public string? VerifyMode { get; set; }
    public string? DeviceSerial { get; set; }
    public bool Synced { get; set; }
    public DateTime? SyncedAt { get; set; }
    public int SyncAttempts { get; set; }
    public string? LastSyncError { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public bool PermanentFailure { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}

public class LocalDevice
{
    public int Id { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string? FirmwareVersion { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public int UserCount { get; set; }
    public int FaceCount { get; set; }
    public int FingerprintCount { get; set; }
    public int LogCount { get; set; }
    /// <summary>Commands queued for delivery to the device on its next poll (push protocol).</summary>
    public string? PendingCommands { get; set; }
}

/// <summary>Biometric templates captured on-device, queued for upload to central HRIS.</summary>
public class LocalTemplateUpload
{
    public int Id { get; set; }
    public string BiometricUserId { get; set; } = string.Empty;
    public int Type { get; set; }
    public int FingerIndex { get; set; }
    public string TemplateData { get; set; } = string.Empty;
    public string? DeviceSerial { get; set; }
    public int? EnrollmentId { get; set; }
    public bool Synced { get; set; }
    public int SyncAttempts { get; set; }
    public string? LastSyncError { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}

public class GatewayState
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
}
