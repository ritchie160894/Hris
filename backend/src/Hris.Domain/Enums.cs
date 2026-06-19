namespace Hris.Domain;

public enum UserRole
{
    SuperAdministrator = 1,
    HrAdministrator = 2,
    PayrollOfficer = 3,
    DepartmentHead = 4,
    Supervisor = 5,
    Employee = 6,
    HrOfficer = 7,
    VicePresidentHrHead = 8,
    PresidentCeo = 9
}

public enum EmploymentStatus
{
    Probationary = 1,
    Regular = 2,
    Contractual = 3,
    ProjectBased = 4,
    Resigned = 5,
    Terminated = 6,
    Retired = 7,
    OnLeave = 8
}

public enum PayType { Monthly = 1, SemiMonthly = 2, Daily = 3, Hourly = 4 }

public enum PunchType { TimeIn = 1, TimeOut = 2, BreakIn = 3, BreakOut = 4 }

public enum AttendanceSource { Biometric = 1, Manual = 2, Web = 3, Mobile = 4 }

public enum RequestStatus
{
    Pending = 1,
    InProgress = 2,
    Approved = 3,
    Rejected = 4,
    ReturnedForRevision = 5,
    Cancelled = 6
}

public enum RequestType
{
    Leave = 1,
    ServiceIncentiveLeave = 2,
    Overtime = 3,
    CashAdvance = 4,
    Loan = 5,
    AttendanceCorrection = 6,
    Payroll = 7,
    OvertimeCorrection = 8
}

public enum ApprovalStepStatus { Waiting = 0, Pending = 1, Approved = 2, Rejected = 3, Returned = 4, Skipped = 5 }

public enum AttendanceCorrectionIssue
{
    MissingTimeIn = 1,
    MissingTimeOut = 2,
    IncorrectRecord = 3,
    ForgottenBiometric = 4,
    DeviceFailure = 5,
    Other = 99
}

public enum OvertimeCorrectionIssue
{
    ApprovedOtNotEncoded = 1,
    OtNotInLogs = 2,
    IncorrectOtHours = 3,
    Other = 99
}

public enum LeaveCategory { Vacation = 1, Sick = 2, ServiceIncentive = 3, Maternity = 4, Paternity = 5, Bereavement = 6, Emergency = 7, Unpaid = 8 }

public enum LoanType { CompanyLoan = 1, CashAdvance = 2, SssLoan = 3, PagIbigLoan = 4, Other = 5 }

public enum LoanStatus { PendingApproval = 1, Active = 2, FullyPaid = 3, Rejected = 4, Cancelled = 5 }

public enum PayrollStatus { Draft = 1, Processing = 2, ForApproval = 3, Approved = 4, Released = 5, Closed = 6, ForCeoApproval = 7 }

public enum PayComponentType { Allowance = 1, Deduction = 2, Incentive = 3, Bonus = 4 }

/// <summary>Semi-monthly cutoff when a deduction type applies.</summary>
public enum PayrollCutoffHalf { Both = 0, FirstHalfOnly = 1, SecondHalfOnly = 2 }

/// <summary>How often a recurring employee deduction runs.</summary>
public enum DeductionFrequency
{
    EveryCutoff = 1,
    Monthly = 2,
    FirstHalfOnly = 3,
    SecondHalfOnly = 4,
    FixedInstallments = 5
}

public enum BenefitType { Hmo = 1, Allowance = 2, Incentive = 3, Bonus = 4, Other = 5 }

public enum DeviceStatus { Online = 1, Offline = 2, Error = 3, Unregistered = 4 }

public enum BiometricTemplateType { Face = 1, Fingerprint = 2 }

public enum BiometricEnrollmentStatus
{
    Pending = 1,
    WaitingOnDevice = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
    Expired = 6
}

public enum SyncDirection { SiteToCentral = 1, CentralToSite = 2 }

public enum SyncStatus { Pending = 1, InProgress = 2, Completed = 3, Failed = 4, Conflict = 5 }

public enum ConflictResolution { Unresolved = 0, KeptCentral = 1, KeptSite = 2, Merged = 3, Discarded = 4 }

public enum NotificationType
{
    General = 0,
    ApprovalRequest = 1,
    ApprovalResult = 2,
    Attendance = 3,
    Payroll = 4,
    Leave = 5,
    Overtime = 6,
    Announcement = 7,
    Device = 8,
    System = 9
}

public enum NotificationChannel { InApp = 1, Email = 2 }

public enum NotificationDeliveryStatus { Queued = 1, Delivered = 2, Failed = 3 }

public enum AuditCategory { Login = 1, Activity = 2, RecordChange = 3, Approval = 4, Payroll = 5, Device = 6, Sync = 7, Security = 8 }

public enum AnnouncementType { Announcement = 1, Memo = 2, Event = 3, HolidayNotice = 4 }

public enum HolidayType { Regular = 1, SpecialNonWorking = 2, SpecialWorking = 3 }

public enum ApplicantStatus { Applied = 1, Screening = 2, Interview = 3, Offer = 4, Hired = 5, Rejected = 6, Withdrawn = 7 }

public enum JobPostingStatus { Draft = 1, Open = 2, OnHold = 3, Closed = 4 }

public enum TrainingStatus { Planned = 1, Ongoing = 2, Completed = 3, Cancelled = 4 }

public enum DocumentCategory { Contract = 1, Certificate = 2, GovernmentId = 3, Memo = 4, Policy = 5, Other = 6 }

public enum TaxStatus { Single = 1, Married = 2, HeadOfFamily = 3 }
