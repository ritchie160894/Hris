namespace Hris.Domain.Entities;

// ---- Security / users ----

public class User : BaseEntity
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Employee;
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public int FailedLoginAttempts { get; set; }
    public bool IsLocked { get; set; }
}

public class AuditLog : BaseEntity
{
    public AuditCategory Category { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
}

// ---- Notifications ----

public class Notification : BaseEntity
{
    public int UserId { get; set; }
    public User? User { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }
    public bool IsRead { get; set; }
    public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;
    public NotificationDeliveryStatus DeliveryStatus { get; set; } = NotificationDeliveryStatus.Delivered;
    public int RetryCount { get; set; }
    public DateTime? DeliveredAt { get; set; }
}

// ---- Announcements ----

public class Announcement : BaseEntity
{
    public AnnouncementType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateOnly PublishDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public bool IsPinned { get; set; }
    public string? PostedByName { get; set; }
    public bool IsActive { get; set; } = true;
}

// ---- Recruitment ----

public class JobPosting : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public int? PositionId { get; set; }
    public Position? Position { get; set; }
    public string? Description { get; set; }
    public string? Requirements { get; set; }
    public int Vacancies { get; set; } = 1;
    public JobPostingStatus Status { get; set; } = JobPostingStatus.Draft;
    public DateOnly? PostedDate { get; set; }
    public DateOnly? ClosingDate { get; set; }
    public List<Applicant> Applicants { get; set; } = new();
}

public class Applicant : BaseEntity
{
    public int JobPostingId { get; set; }
    public JobPosting? JobPosting { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? ContactNumber { get; set; }
    public string? ResumePath { get; set; }
    public ApplicantStatus Status { get; set; } = ApplicantStatus.Applied;
    public string? Notes { get; set; }
    public List<Interview> Interviews { get; set; } = new();
}

public class Interview : BaseEntity
{
    public int ApplicantId { get; set; }
    public Applicant? Applicant { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string? InterviewerName { get; set; }
    public string? Location { get; set; }
    public string? Result { get; set; }
    public string? Feedback { get; set; }
    public bool IsCompleted { get; set; }
}

// ---- Performance ----

public class PerformanceReview : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public string Period { get; set; } = string.Empty; // e.g. "2026 H1"
    public string? ReviewerName { get; set; }
    public DateOnly ReviewDate { get; set; }
    public decimal OverallScore { get; set; }
    public string? Strengths { get; set; }
    public string? AreasForImprovement { get; set; }
    public string? Comments { get; set; }
    public bool IsFinalized { get; set; }
    public List<KpiScore> KpiScores { get; set; } = new();
}

public class KpiScore : BaseEntity
{
    public int PerformanceReviewId { get; set; }
    public PerformanceReview? PerformanceReview { get; set; }
    public string KpiName { get; set; } = string.Empty;
    public decimal Weight { get; set; }
    public decimal Score { get; set; }
    public string? Remarks { get; set; }
}

// ---- Training ----

public class Training : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? Description { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Location { get; set; }
    public TrainingStatus Status { get; set; } = TrainingStatus.Planned;
    public decimal? Cost { get; set; }
    public List<TrainingParticipant> Participants { get; set; } = new();
}

public class TrainingParticipant : BaseEntity
{
    public int TrainingId { get; set; }
    public Training? Training { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public bool Completed { get; set; }
    public string? CertificateNumber { get; set; }
    public DateOnly? CertificateExpiry { get; set; }
}

// ---- Synchronization ----

public class SyncBatch : BaseEntity
{
    public int SiteId { get; set; }
    public Site? Site { get; set; }
    public SyncDirection Direction { get; set; }
    public string DataType { get; set; } = string.Empty; // Attendance, Employee, Template...
    public int RecordCount { get; set; }
    public int DuplicateCount { get; set; }
    public int ConflictCount { get; set; }
    public SyncStatus Status { get; set; } = SyncStatus.Pending;
    public string? ErrorMessage { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class SyncConflict : BaseEntity
{
    public int SiteId { get; set; }
    public Site? Site { get; set; }
    public string DataType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public ConflictResolution Resolution { get; set; } = ConflictResolution.Unresolved;
    public string? ResolvedByName { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

/// <summary>Key-value store for admin-configurable system options.</summary>
public class SystemSetting : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
