namespace Hris.Domain.Entities;

public class Employee : BaseEntity
{
    public string EmployeeCode { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string? Suffix { get; set; }
    public string? PhotoUrl { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Gender { get; set; }
    public string? CivilStatus { get; set; }
    public string? Address { get; set; }
    public string? ContactNumber { get; set; }
    public string? Email { get; set; }

    // Employment
    public DateOnly HireDate { get; set; }
    public DateOnly? RegularizationDate { get; set; }
    public DateOnly? SeparationDate { get; set; }
    public EmploymentStatus Status { get; set; } = EmploymentStatus.Probationary;
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public int? PositionId { get; set; }
    public Position? Position { get; set; }
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public int? SiteId { get; set; }
    public Site? Site { get; set; }
    public int? ManagerId { get; set; }
    public Employee? Manager { get; set; }

    // Compensation
    public PayType PayType { get; set; } = PayType.SemiMonthly;
    public decimal MonthlySalary { get; set; }
    public decimal? DailyRate { get; set; }
    public TaxStatus TaxStatus { get; set; } = TaxStatus.Single;

    // Government numbers
    public string? SssNumber { get; set; }
    public string? PhilHealthNumber { get; set; }
    public string? PagIbigNumber { get; set; }
    public string? Tin { get; set; }

    /// <summary>When true, payroll uses the manual monthly statutory amounts below instead of table-based computation.</summary>
    public bool UseManualStatutoryContributions { get; set; }
    public decimal ManualSssEmployee { get; set; }
    public decimal ManualSssEmployer { get; set; }
    public decimal ManualPhilHealthEmployee { get; set; }
    public decimal ManualPhilHealthEmployer { get; set; }
    public decimal ManualPagIbigEmployee { get; set; }
    public decimal ManualPagIbigEmployer { get; set; }
    public decimal ManualWithholdingTax { get; set; }

    // Schedule (default shift)
    public TimeOnly ShiftStart { get; set; } = new(8, 0);
    public TimeOnly ShiftEnd { get; set; } = new(17, 0);
    public int BreakMinutes { get; set; } = 60;
    public string WorkDays { get; set; } = "Mon,Tue,Wed,Thu,Fri";

    /// <summary>Biometric user id assigned on SenseFace devices (usually employee code).</summary>
    public string? BiometricUserId { get; set; }

    public byte[] RowVersionStamp { get; set; } = Array.Empty<byte>();

    public List<EmergencyContact> EmergencyContacts { get; set; } = new();
    public List<EmployeeDocument> Documents { get; set; } = new();
    public List<BiometricTemplate> BiometricTemplates { get; set; } = new();

    public string FullName => string.Join(" ", new[] { FirstName, MiddleName, LastName, Suffix }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public class EmergencyContact : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string? Address { get; set; }
}

public class EmployeeDocument : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DocumentCategory Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? Notes { get; set; }
}

public class EmployeeHistory : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public string EventType { get; set; } = string.Empty; // Hired, Promoted, Transferred, SalaryChange, StatusChange...
    public string Description { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }
    public string? ChangedByUserName { get; set; }
}

public class BiometricTemplate : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public BiometricTemplateType Type { get; set; }
    public int FingerIndex { get; set; } // 0 for face; 0-9 for fingerprint
    public string TemplateData { get; set; } = string.Empty; // base64 ZKTeco template
    public int Version { get; set; } = 1;
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public int? DeviceId { get; set; }
    public BiometricDevice? Device { get; set; }
    public string? CapturedOnDeviceSerial { get; set; }
}

/// <summary>Tracks face/fingerprint enrollment sessions initiated from HRIS until the device captures a template.</summary>
public class BiometricEnrollment : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public int DeviceId { get; set; }
    public BiometricDevice? Device { get; set; }
    public BiometricTemplateType Type { get; set; }
    public int FingerIndex { get; set; }
    public BiometricEnrollmentStatus Status { get; set; } = BiometricEnrollmentStatus.Pending;
    public string? RequestedByUserName { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int? ResultTemplateId { get; set; }
    /// <summary>When true, the site gateway has queued the iclock command on the device.</summary>
    public bool DispatchedToGateway { get; set; }
    /// <summary>ZKTeco PUSH command sent to the device (ENROLL FP / ENROLL BIO).</summary>
    public string? DeviceCommand { get; set; }
}
