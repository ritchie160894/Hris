namespace Hris.Domain.Entities;

/// <summary>Master list of payroll deduction categories (per cutoff schedule).</summary>
public class DeductionType : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Which semi-monthly cutoffs this deduction may appear on.</summary>
    public PayrollCutoffHalf ApplicableHalf { get; set; } = PayrollCutoffHalf.Both;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Recurring deduction assigned to an employee (amount entered once).</summary>
public class EmployeeDeduction : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public int DeductionTypeId { get; set; }
    public DeductionType? DeductionType { get; set; }
    /// <summary>Optional link to an active loan (company / cash advance / gov't loan).</summary>
    public int? LoanId { get; set; }
    public Loan? Loan { get; set; }
    public decimal Amount { get; set; }
    public decimal? RemainingBalance { get; set; }
    public int? TotalInstallments { get; set; }
    public int PaidInstallments { get; set; }
    public DeductionFrequency Frequency { get; set; } = DeductionFrequency.EveryCutoff;
    /// <summary>Profile checkbox — deduction is eligible for payroll when enabled.</summary>
    public bool IsProfileEnabled { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

/// <summary>Per cutoff + employee: which deductions are checked for this payroll run.</summary>
public class PayrollCutoffDeductionSelection : BaseEntity
{
    public int PayrollCutoffId { get; set; }
    public PayrollCutoff? PayrollCutoff { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public int EmployeeDeductionId { get; set; }
    public EmployeeDeduction? EmployeeDeduction { get; set; }
    public bool IsApplied { get; set; } = true;
}

public class DeductionTemplate : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public List<DeductionTemplateItem> Items { get; set; } = new();
}

public class DeductionTemplateItem : BaseEntity
{
    public int TemplateId { get; set; }
    public DeductionTemplate? Template { get; set; }
    public int DeductionTypeId { get; set; }
    public DeductionType? DeductionType { get; set; }
}
