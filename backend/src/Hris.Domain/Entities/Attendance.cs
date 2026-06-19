namespace Hris.Domain.Entities;

public class AttendanceLog : BaseEntity
{
    /// <summary>Globally unique id used for duplicate detection across sites.</summary>
    public Guid SyncGuid { get; set; } = Guid.NewGuid();
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public int? SiteId { get; set; }
    public Site? Site { get; set; }
    public int? DeviceId { get; set; }
    public BiometricDevice? Device { get; set; }
    public DateTime PunchTime { get; set; }
    /// <summary>Denormalized date for fast range queries and year-based partitioning.</summary>
    public DateOnly AttendanceDate { get; set; }
    public int AttendanceYear { get; set; }
    public PunchType PunchType { get; set; }
    public AttendanceSource Source { get; set; } = AttendanceSource.Biometric;
    public string? VerifyMode { get; set; } // face, fingerprint, card, password
    public bool IsCorrected { get; set; }
    public string? Remarks { get; set; }
    public DateTime? SyncedAt { get; set; }
}

/// <summary>
/// Pre-aggregated daily attendance used by payroll, reports, and monitoring.
/// Payroll reads this table — not raw AttendanceLogs — for fast cutoff processing.
/// </summary>
public class AttendanceDailySummary : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public int AttendanceYear { get; set; }
    public DateTime? TimeIn { get; set; }
    public DateTime? TimeOut { get; set; }
    public DateTime? BreakOut { get; set; }
    public DateTime? BreakIn { get; set; }
    public decimal HoursWorked { get; set; }
    public decimal LateMinutes { get; set; }
    public decimal UndertimeMinutes { get; set; }
    /// <summary>Present, Late, Absent, OnLeave, Holiday, RestDay</summary>
    public string Status { get; set; } = "Absent";
    public bool HasTimeIn { get; set; }
    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Cold storage for attendance logs older than the active retention window.</summary>
public class AttendanceLogArchive : BaseEntity
{
    public Guid SyncGuid { get; set; }
    public int EmployeeId { get; set; }
    public int? SiteId { get; set; }
    public int? DeviceId { get; set; }
    public DateTime PunchTime { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public int AttendanceYear { get; set; }
    public PunchType PunchType { get; set; }
    public AttendanceSource Source { get; set; }
    public string? VerifyMode { get; set; }
    public bool IsCorrected { get; set; }
    public string? Remarks { get; set; }
    public DateTime? SyncedAt { get; set; }
    public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;
}

public class AttendanceCorrection : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public PunchType PunchType { get; set; }
    public AttendanceCorrectionIssue IssueType { get; set; } = AttendanceCorrectionIssue.Other;
    public DateTime? OriginalTime { get; set; }
    public DateTime CorrectedTime { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? SupportingDocument { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public int CurrentApprovalLevel { get; set; } = 1;
    public int RequestedByUserId { get; set; }
    public int? ApprovedByUserId { get; set; }
    public DateTime? ActedAt { get; set; }
    public DateTime? PayrollAppliedAt { get; set; }
    public string? ApproverRemarks { get; set; }
}

public class BiometricDevice : BaseEntity
{
    public string SerialNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = "SenseFace 2A";
    public int? SiteId { get; set; }
    public Site? Site { get; set; }
    public string? IpAddress { get; set; }
    public int Port { get; set; } = 4370;
    public DeviceStatus Status { get; set; } = DeviceStatus.Unregistered;
    public DateTime? LastSeenAt { get; set; }
    public string? FirmwareVersion { get; set; }
    public int UserCount { get; set; }
    public int FaceCount { get; set; }
    public int FingerprintCount { get; set; }
    public int LogCount { get; set; }
    public bool IsActive { get; set; } = true;
}

public class DeviceActivityLog : BaseEntity
{
    public int DeviceId { get; set; }
    public BiometricDevice? Device { get; set; }
    public string Activity { get; set; } = string.Empty;
    public string? Details { get; set; }
    public bool IsError { get; set; }
}
