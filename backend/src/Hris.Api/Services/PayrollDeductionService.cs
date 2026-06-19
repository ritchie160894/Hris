using Hris.Api.Data;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Services;

public record DeductionLine(string Code, string Name, decimal Amount, int? EmployeeDeductionId, int? LoanId);

public class PayrollDeductionService(HrisDbContext db)
{
    public static bool TypeAppliesToCutoff(DeductionType t, bool isFirstHalf) =>
        t.ApplicableHalf switch
        {
            PayrollCutoffHalf.Both => true,
            PayrollCutoffHalf.FirstHalfOnly => isFirstHalf,
            PayrollCutoffHalf.SecondHalfOnly => !isFirstHalf,
            _ => true
        };

    public static bool FrequencyApplies(DeductionFrequency freq, bool isFirstHalf) =>
        freq switch
        {
            DeductionFrequency.EveryCutoff => true,
            DeductionFrequency.Monthly => isFirstHalf,
            DeductionFrequency.FirstHalfOnly => isFirstHalf,
            DeductionFrequency.SecondHalfOnly => !isFirstHalf,
            DeductionFrequency.FixedInstallments => true,
            _ => true
        };

    public static bool IsLoanDeductionCode(string code) =>
        code is "LOAN" or "COMPANY_LOAN" or "CASH_ADVANCE" or "SSS_CL" or "SSS_SL" or "HDMF_SR" or "HDMF_CC";

    public static bool IsOtherDeductionCode(string code) =>
        code is "CHARGES" or "RICE" or "MONTHLY_BILLS";

    /// <summary>Cutoff-level master switch for a deduction category (AND with employee profile + checklist).</summary>
    public static bool CutoffAllowsDeductionType(string code, PayrollCutoff cutoff) =>
        code switch
        {
            "SSS" => cutoff.DeductSss,
            "PHILHEALTH" => cutoff.DeductPhilHealth,
            "HDMF" => cutoff.DeductPagIbig,
            "WITHHOLDING_TAX" => cutoff.DeductTax,
            _ when IsLoanDeductionCode(code) => cutoff.DeductLoans,
            _ when IsOtherDeductionCode(code) => cutoff.DeductOtherDeductions,
            _ => true
        };

    public async Task EnsureDeductionTypesSeededAsync(CancellationToken ct = default)
    {
        if (await db.DeductionTypes.AnyAsync(ct)) return;

        var types = new[]
        {
            new DeductionType { Code = "COMPANY_LOAN", Name = "Company Loan", ApplicableHalf = PayrollCutoffHalf.Both, SortOrder = 1 },
            new DeductionType { Code = "CASH_ADVANCE", Name = "Cash Advance", ApplicableHalf = PayrollCutoffHalf.Both, SortOrder = 2 },
            new DeductionType { Code = "CHARGES", Name = "Charges", ApplicableHalf = PayrollCutoffHalf.Both, SortOrder = 3 },
            new DeductionType { Code = "RICE", Name = "Rice Allowance Deduction", ApplicableHalf = PayrollCutoffHalf.Both, SortOrder = 4 },
            new DeductionType { Code = "MONTHLY_BILLS", Name = "Monthly Bills", ApplicableHalf = PayrollCutoffHalf.Both, SortOrder = 5 },
            new DeductionType { Code = "SSS", Name = "SSS Contribution", ApplicableHalf = PayrollCutoffHalf.SecondHalfOnly, SortOrder = 10 },
            new DeductionType { Code = "PHILHEALTH", Name = "PhilHealth Contribution", ApplicableHalf = PayrollCutoffHalf.SecondHalfOnly, SortOrder = 11 },
            new DeductionType { Code = "HDMF", Name = "HDMF Contribution", ApplicableHalf = PayrollCutoffHalf.SecondHalfOnly, SortOrder = 12 },
            new DeductionType { Code = "SSS_CL", Name = "SSS Calamity Loan", ApplicableHalf = PayrollCutoffHalf.FirstHalfOnly, SortOrder = 20 },
            new DeductionType { Code = "SSS_SL", Name = "SSS Salary Loan", ApplicableHalf = PayrollCutoffHalf.FirstHalfOnly, SortOrder = 21 },
            new DeductionType { Code = "HDMF_SR", Name = "HDMF Salary Loan", ApplicableHalf = PayrollCutoffHalf.FirstHalfOnly, SortOrder = 22 },
            new DeductionType { Code = "HDMF_CC", Name = "HDMF Calamity Loan", ApplicableHalf = PayrollCutoffHalf.FirstHalfOnly, SortOrder = 23 },
            new DeductionType { Code = "WITHHOLDING_TAX", Name = "Withholding Tax", ApplicableHalf = PayrollCutoffHalf.FirstHalfOnly, SortOrder = 30 },
        };
        db.DeductionTypes.AddRange(types);
        await db.SaveChangesAsync(ct);

        var withLoan = new DeductionTemplate { Name = "Employee with Loan", Description = "Company loan and rice deduction." };
        db.DeductionTemplates.Add(new DeductionTemplate { Name = "Regular Employee", Description = "Standard statutory via cutoff flags." });
        db.DeductionTemplates.Add(withLoan);
        await db.SaveChangesAsync(ct);

        var companyLoan = types.First(t => t.Code == "COMPANY_LOAN");
        var rice = types.First(t => t.Code == "RICE");
        db.DeductionTemplateItems.AddRange(
            new DeductionTemplateItem { TemplateId = withLoan.Id, DeductionTypeId = companyLoan.Id },
            new DeductionTemplateItem { TemplateId = withLoan.Id, DeductionTypeId = rice.Id });
        await db.SaveChangesAsync(ct);
    }

    public async Task SyncLoanDeductionsAsync(int employeeId, CancellationToken ct = default)
    {
        var types = await db.DeductionTypes.ToDictionaryAsync(t => t.Code, ct);
        var loans = await db.Loans
            .Where(l => l.EmployeeId == employeeId && l.Status == LoanStatus.Active && l.Balance > 0)
            .ToListAsync(ct);

        foreach (var loan in loans)
        {
            var code = loan.Type switch
            {
                LoanType.CompanyLoan => "COMPANY_LOAN",
                LoanType.CashAdvance => "CASH_ADVANCE",
                LoanType.SssLoan => "SSS_SL",
                LoanType.PagIbigLoan => "HDMF_SR",
                _ => "COMPANY_LOAN"
            };
            if (!types.TryGetValue(code, out var dt)) continue;

            var existing = await db.EmployeeDeductions.FirstOrDefaultAsync(d => d.EmployeeId == employeeId && d.LoanId == loan.Id, ct);
            if (existing is null)
            {
                db.EmployeeDeductions.Add(new EmployeeDeduction
                {
                    EmployeeId = employeeId,
                    DeductionTypeId = dt.Id,
                    LoanId = loan.Id,
                    Amount = loan.AmortizationPerCutoff,
                    RemainingBalance = loan.Balance,
                    TotalInstallments = loan.AmortizationPerCutoff > 0
                        ? (int)Math.Ceiling(loan.Balance / loan.AmortizationPerCutoff) : null,
                    Frequency = DeductionFrequency.FixedInstallments,
                    IsProfileEnabled = true,
                    IsActive = true,
                    EffectiveFrom = loan.StartDate
                });
            }
            else
            {
                existing.Amount = loan.AmortizationPerCutoff;
                existing.RemainingBalance = loan.Balance;
                existing.IsActive = true;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task InitializeCutoffSelectionsAsync(int cutoffId, CancellationToken ct = default)
    {
        var cutoff = await db.PayrollCutoffs.FindAsync([cutoffId], ct)
            ?? throw new InvalidOperationException("Cutoff not found.");
        var isFirstHalf = cutoff.PeriodStart.Day <= 15;
        var execIds = await ExecutiveExemption.GetExemptEmployeeIdsAsync(db);

        var types = await db.DeductionTypes.Where(t => t.IsActive).ToListAsync(ct);
        var applicableTypeIds = types.Where(t => TypeAppliesToCutoff(t, isFirstHalf)).Select(t => t.Id).ToHashSet();

        var deductions = await db.EmployeeDeductions
            .Where(d => d.IsActive && d.IsProfileEnabled && applicableTypeIds.Contains(d.DeductionTypeId))
            .Where(d => (d.EffectiveFrom == null || d.EffectiveFrom <= cutoff.PeriodEnd)
                && (d.EffectiveTo == null || d.EffectiveTo >= cutoff.PeriodStart))
            .Where(d => !execIds.Contains(d.EmployeeId))
            .ToListAsync(ct);

        var existing = await db.PayrollCutoffDeductionSelections
            .Where(s => s.PayrollCutoffId == cutoffId)
            .Select(s => s.EmployeeDeductionId)
            .ToHashSetAsync(ct);

        foreach (var d in deductions)
        {
            if (existing.Contains(d.Id)) continue;
            if (!FrequencyApplies(d.Frequency, isFirstHalf)) continue;
            if (d.Frequency == DeductionFrequency.FixedInstallments && d.RemainingBalance is <= 0) continue;

            var type = types.First(t => t.Id == d.DeductionTypeId);
            if (!CutoffAllowsDeductionType(type.Code, cutoff)) continue;

            db.PayrollCutoffDeductionSelections.Add(new PayrollCutoffDeductionSelection
            {
                PayrollCutoffId = cutoffId,
                EmployeeId = d.EmployeeId,
                EmployeeDeductionId = d.Id,
                IsApplied = true
            });
        }
        await db.SaveChangesAsync(ct);
    }

    public List<DeductionLine> ComputeForEmployee(
        Employee emp,
        bool isFirstHalf,
        PayrollCutoff cutoff,
        List<DeductionType> types,
        List<EmployeeDeduction> empDeductions,
        Dictionary<int, PayrollCutoffDeductionSelection> selectionsByDeductionId,
        List<Loan> loans,
        bool cutoffUsesChecklist,
        bool useLegacyLoans)
    {
        var lines = new List<DeductionLine>();
        var applicableTypes = types.Where(t => t.IsActive && TypeAppliesToCutoff(t, isFirstHalf)).ToDictionary(t => t.Id);
        var hasManagedLoanDeduction = false;

        foreach (var d in empDeductions.Where(x => x.EmployeeId == emp.Id && x.IsActive && x.IsProfileEnabled))
        {
            if (!applicableTypes.ContainsKey(d.DeductionTypeId)) continue;
            if (!FrequencyApplies(d.Frequency, isFirstHalf)) continue;
            if (d.EffectiveFrom.HasValue && d.EffectiveFrom > cutoff.PeriodEnd) continue;
            if (d.EffectiveTo.HasValue && d.EffectiveTo < cutoff.PeriodStart) continue;

            var applied = cutoffUsesChecklist
                ? selectionsByDeductionId.TryGetValue(d.Id, out var sel) && sel.IsApplied
                : selectionsByDeductionId.TryGetValue(d.Id, out var sel2) ? sel2.IsApplied : true;
            if (!applied) continue;

            var type = applicableTypes[d.DeductionTypeId];
            if (!CutoffAllowsDeductionType(type.Code, cutoff)) continue;
            var amount = ResolveAmount(d, loans);
            if (amount <= 0) continue;

            if (d.LoanId.HasValue) hasManagedLoanDeduction = true;
            lines.Add(new DeductionLine(type.Code, type.Name, amount, d.Id, d.LoanId));
        }

        if (useLegacyLoans && cutoff.DeductLoans && !hasManagedLoanDeduction)
        {
            foreach (var loan in loans.Where(l => l.EmployeeId == emp.Id))
            {
                var isGov = loan.Type is LoanType.SssLoan or LoanType.PagIbigLoan;
                if (isGov && !isFirstHalf) continue;
                var amount = Math.Min(loan.AmortizationPerCutoff, loan.Balance);
                if (amount <= 0) continue;
                lines.Add(new DeductionLine("LOAN", $"{loan.Type} ({loan.Reference})", amount, null, loan.Id));
            }
        }

        return lines;
    }

    public static decimal ResolveAmount(EmployeeDeduction d, List<Loan> loans)
    {
        if (d.LoanId is int lid)
        {
            var loan = loans.FirstOrDefault(l => l.Id == lid);
            if (loan is null || loan.Balance <= 0) return 0;
            var amt = Math.Min(d.Amount > 0 ? d.Amount : loan.AmortizationPerCutoff, loan.Balance);
            if (d.RemainingBalance.HasValue) amt = Math.Min(amt, d.RemainingBalance.Value);
            return amt;
        }

        if (d.Frequency == DeductionFrequency.FixedInstallments)
        {
            if (d.RemainingBalance is <= 0) return 0;
            if (d.TotalInstallments.HasValue && d.PaidInstallments >= d.TotalInstallments.Value) return 0;
            return Math.Min(d.Amount, d.RemainingBalance ?? d.Amount);
        }

        return d.Amount;
    }

    public async Task PostInstallmentPaymentsAsync(PayrollCutoff cutoff, Payslip slip, List<DeductionLine> lines, CancellationToken ct = default)
    {
        var loans = await db.Loans.Where(l => l.Status == LoanStatus.Active).ToListAsync(ct);
        var empDeductions = await db.EmployeeDeductions.Where(d => d.EmployeeId == slip.EmployeeId).ToListAsync(ct);

        foreach (var line in lines)
        {
            if (line.LoanId is int loanId)
            {
                var loan = loans.FirstOrDefault(l => l.Id == loanId);
                if (loan is null || line.Amount <= 0) continue;
                loan.Balance -= line.Amount;
                if (loan.Balance <= 0) loan.Status = LoanStatus.FullyPaid;
                db.LoanPayments.Add(new LoanPayment
                {
                    LoanId = loanId,
                    PayslipId = slip.Id,
                    PaymentDate = cutoff.PayDate,
                    Amount = line.Amount,
                    Remarks = $"Payroll deduction - {cutoff.Name}"
                });
            }

            if (line.EmployeeDeductionId is int edId)
            {
                var ed = empDeductions.FirstOrDefault(x => x.Id == edId);
                if (ed is null) continue;
                if (ed.Frequency == DeductionFrequency.FixedInstallments || ed.RemainingBalance.HasValue)
                {
                    ed.PaidInstallments++;
                    if (ed.RemainingBalance.HasValue)
                    {
                        ed.RemainingBalance = Math.Max(0, ed.RemainingBalance.Value - line.Amount);
                        if (ed.RemainingBalance <= 0) ed.IsActive = false;
                    }
                    else if (ed.TotalInstallments.HasValue && ed.PaidInstallments >= ed.TotalInstallments.Value)
                        ed.IsActive = false;
                }
            }
        }
    }
}
