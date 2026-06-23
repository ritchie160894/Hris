namespace Hris.Domain.Entities;

public class PayrollCutoff : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public DateOnly PayDate { get; set; }
    public PayrollStatus Status { get; set; } = PayrollStatus.Draft;
    public bool DeductSss { get; set; } = true;
    public bool DeductPhilHealth { get; set; } = true;
    public bool DeductPagIbig { get; set; } = true;
    public bool DeductTax { get; set; } = true;
    public bool DeductLoans { get; set; } = true;
    /// <summary>Rice, bills, charges, and similar recurring employee deductions.</summary>
    public bool DeductOtherDeductions { get; set; } = true;
    public DateTime? ProcessedAt { get; set; }
    public DateTime? VpApprovedAt { get; set; }
    public string? VpApprovedByName { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedByName { get; set; }
    /// <summary>Set when background payroll processing fails.</summary>
    public string? ProcessingError { get; set; }
    public List<Payslip> Payslips { get; set; } = new();
}

public class Payslip : BaseEntity
{
    public int PayrollCutoffId { get; set; }
    public PayrollCutoff? PayrollCutoff { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public decimal BasicPay { get; set; }
    public decimal OvertimePay { get; set; }
    public decimal HolidayPay { get; set; }
    public decimal Allowances { get; set; }
    public decimal Bonuses { get; set; }
    public decimal GrossPay { get; set; }

    public decimal AbsenceDeduction { get; set; }
    public decimal LateDeduction { get; set; }
    public decimal SssEmployee { get; set; }
    public decimal SssEmployer { get; set; }
    public decimal PhilHealthEmployee { get; set; }
    public decimal PhilHealthEmployer { get; set; }
    public decimal PagIbigEmployee { get; set; }
    public decimal PagIbigEmployer { get; set; }
    public decimal WithholdingTax { get; set; }
    public decimal LoanDeductions { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetPay { get; set; }

    public decimal DaysWorked { get; set; }
    public decimal DaysAbsent { get; set; }
    public decimal LateMinutes { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal LeaveDaysPaid { get; set; }

    // Undertime policy: hours / 24 = leave-day equivalent, charged to the annual
    // leave balance first; any portion not covered by credits is deducted from pay.
    public decimal UndertimeHours { get; set; }
    /// <summary>SIL day-fraction auto-charged on payroll release (unfiled undertime).</summary>
    public decimal UndertimeLeaveDays { get; set; }
    /// <summary>EL day-fraction from filed undertime (display only — EL charged on approval).</summary>
    public decimal UndertimeElDays { get; set; }
    public decimal UndertimeDeduction { get; set; }

    /// <summary>JSON breakdown of itemized lines for the payslip view.</summary>
    public string? DetailsJson { get; set; }
}

/// <summary>Recurring or one-time allowance/deduction assigned to an employee.</summary>
public class PayComponent : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public PayComponentType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool PerCutoff { get; set; } = true;
    public bool IsRecurring { get; set; } = true;
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Loan : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public LoanType Type { get; set; }
    public string Reference { get; set; } = string.Empty;
    public decimal Principal { get; set; }
    public decimal Balance { get; set; }
    public decimal AmortizationPerCutoff { get; set; }
    public DateOnly StartDate { get; set; }
    public LoanStatus Status { get; set; } = LoanStatus.PendingApproval;
    public RequestStatus ApprovalStatus { get; set; } = RequestStatus.Pending;
    public int CurrentApprovalLevel { get; set; } = 1;
    public string? Purpose { get; set; }
    public List<LoanPayment> Payments { get; set; } = new();
}

public class LoanPayment : BaseEntity
{
    public int LoanId { get; set; }
    public Loan? Loan { get; set; }
    public int? PayslipId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string? Remarks { get; set; }
}

// ---- Government contribution tables (Philippines) ----

public class SssBracket : BaseEntity
{
    public decimal RangeFrom { get; set; }
    public decimal RangeTo { get; set; }
    public decimal MonthlySalaryCredit { get; set; }
    public decimal EmployeeShare { get; set; }
    public decimal EmployerShare { get; set; }
    public int EffectiveYear { get; set; }
}

public class PhilHealthConfig : BaseEntity
{
    public int EffectiveYear { get; set; }
    public decimal RatePercent { get; set; }       // total premium rate, e.g. 5.0
    public decimal MinSalary { get; set; }
    public decimal MaxSalary { get; set; }
    public decimal EmployeeSharePercent { get; set; } = 50;
}

public class PagIbigConfig : BaseEntity
{
    public int EffectiveYear { get; set; }
    /// <summary>Employee rate when monthly compensation exceeds <see cref="EmployeeLowThreshold"/> (typically 2%).</summary>
    public decimal EmployeeRatePercent { get; set; }
    /// <summary>Employer rate (typically 2%).</summary>
    public decimal EmployerRatePercent { get; set; }
    /// <summary>Maximum compensation used as contribution basis (typically ₱10,000).</summary>
    public decimal MaxCompensation { get; set; }
    /// <summary>Employee rate when monthly compensation is at or below this amount (typically 1%).</summary>
    public decimal EmployeeLowRatePercent { get; set; } = 1;
    /// <summary>Compensation threshold for the lower employee rate (typically ₱1,500).</summary>
    public decimal EmployeeLowThreshold { get; set; } = 1500;
}

public class TaxBracket : BaseEntity
{
    public int EffectiveYear { get; set; }
    /// <summary>Per-month bracket (TRAIN law table).</summary>
    public decimal RangeFrom { get; set; }
    public decimal RangeTo { get; set; }
    public decimal BaseTax { get; set; }
    public decimal RatePercentOverExcess { get; set; }
}

public class Benefit : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public BenefitType Type { get; set; }
    public string? Provider { get; set; }
    public string? Description { get; set; }
    public decimal? MonthlyCost { get; set; }
    /// <summary>When true, payroll accrues basic salary ÷ 12 per cutoff for assigned employees (PD 851).</summary>
    public bool IsThirteenthMonth { get; set; }
    public bool IsActive { get; set; } = true;
}

public class EmployeeBenefit : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public int BenefitId { get; set; }
    public Benefit? Benefit { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? PolicyNumber { get; set; }
    public string? Notes { get; set; }
}
