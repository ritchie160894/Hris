namespace Hris.Domain.Entities;

public class LeaveType : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public LeaveCategory Category { get; set; }
    public decimal DefaultAnnualCredits { get; set; }
    public bool IsPaid { get; set; } = true;
    public bool RequiresCeoApproval { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Unused credits can be converted to cash at year end (SIL policy).</summary>
    public bool IsConvertibleToCash { get; set; }
    /// <summary>Only employees with Regular status may avail (SIL policy).</summary>
    public bool RegularEmployeesOnly { get; set; }
}

public class LeaveBalance : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public int LeaveTypeId { get; set; }
    public LeaveType? LeaveType { get; set; }
    public int Year { get; set; }
    public decimal Credits { get; set; }
    public decimal Used { get; set; }
    public decimal Remaining => Credits - Used;
}

public class LeaveRequest : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public int LeaveTypeId { get; set; }
    public LeaveType? LeaveType { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal Days { get; set; }
    public bool IsHalfDay { get; set; }
    /// <summary>Early-departure SIL filing — uses SIL workflow; credits charged on approval.</summary>
    public bool IsUndertime { get; set; }
    public decimal UndertimeHours { get; set; }
    public string Reason { get; set; } = string.Empty;
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public int CurrentApprovalLevel { get; set; } = 1;
    public DateTime? CompletedAt { get; set; }
}

public class OvertimeRequest : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateOnly OvertimeDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public decimal Hours { get; set; }
    public string Reason { get; set; } = string.Empty;
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public int CurrentApprovalLevel { get; set; } = 1;
    public decimal? ComputedPay { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>Correct missed or incorrect OT encoding after Dept Head + HR approval.</summary>
public class OvertimeCorrection : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateOnly OvertimeDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public decimal Hours { get; set; }
    public OvertimeCorrectionIssue IssueType { get; set; } = OvertimeCorrectionIssue.Other;
    public string Reason { get; set; } = string.Empty;
    public string? SupportingDocument { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public int CurrentApprovalLevel { get; set; } = 1;
    public int RequestedByUserId { get; set; }
    public DateTime? PayrollAppliedAt { get; set; }
    public int? CreatedOvertimeRequestId { get; set; }
    public string? ApproverRemarks { get; set; }
}

/// <summary>Defines the chain of approvers for a request type (seeded per company policy).</summary>
public class WorkflowTemplateStep : BaseEntity
{
    public RequestType RequestType { get; set; }
    public int Level { get; set; }
    public UserRole ApproverRole { get; set; }
    public string StepName { get; set; } = string.Empty;
    public bool IsOptional { get; set; }
    /// <summary>Payroll apply step — acknowledge only, cannot reject.</summary>
    public bool IsApplyOnlyStep { get; set; }
}

/// <summary>One approval action instance attached to a specific request.</summary>
public class ApprovalAction : BaseEntity
{
    public RequestType RequestType { get; set; }
    public int RequestId { get; set; }
    public int Level { get; set; }
    public UserRole ApproverRole { get; set; }
    public string StepName { get; set; } = string.Empty;
    public ApprovalStepStatus Status { get; set; } = ApprovalStepStatus.Waiting;
    public int? ActedByUserId { get; set; }
    public string? ActedByName { get; set; }
    public string? Remarks { get; set; }
    public DateTime? ActedAt { get; set; }
    /// <summary>Soft-hide from approval history lists without removing the workflow chain.</summary>
    public bool HiddenFromHistory { get; set; }
}
