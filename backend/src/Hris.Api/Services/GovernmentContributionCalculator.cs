using Hris.Domain.Entities;

namespace Hris.Api.Services;

/// <summary>
/// Philippine statutory contribution and withholding-tax computation (SSS, PhilHealth, Pag-IBIG, TRAIN).
/// Tables are loaded from the database so rates can be updated each year without code changes.
/// </summary>
public static class GovernmentContributionCalculator
{
    public record MonthlyContributions(
        decimal SssEmployee,
        decimal SssEmployer,
        decimal PhilHealthEmployee,
        decimal PhilHealthEmployer,
        decimal PagIbigEmployee,
        decimal PagIbigEmployer,
        decimal WithholdingTax);

    /// <summary>Full monthly statutory amounts used for payroll and remittance.</summary>
    public static MonthlyContributions ComputeMonthly(
        decimal monthlyBasicSalary,
        IReadOnlyList<SssBracket> sssTable,
        PhilHealthConfig? philHealth,
        PagIbigConfig? pagIbig,
        IReadOnlyList<TaxBracket> taxTable,
        bool includeWithholdingTax = true)
    {
        var (sssEe, sssEr) = ComputeSss(monthlyBasicSalary, sssTable);
        var (phEe, phEr) = ComputePhilHealth(monthlyBasicSalary, philHealth);
        var (piEe, piEr) = ComputePagIbig(monthlyBasicSalary, pagIbig);
        var tax = includeWithholdingTax
            ? ComputeWithholdingTax(monthlyBasicSalary, sssEe, phEe, piEe, taxTable)
            : 0;

        return new MonthlyContributions(sssEe, sssEr, phEe, phEr, piEe, piEr, tax);
    }

    /// <summary>
    /// SSS: lookup Monthly Salary Credit (MSC) bracket from monthly basic compensation.
    /// Seeded table follows the 15% schedule (5% employee / 10% employer on MSC).
    /// </summary>
    public static (decimal Employee, decimal Employer) ComputeSss(decimal monthlyBasicSalary, IReadOnlyList<SssBracket> sssTable)
    {
        if (sssTable.Count == 0) return (0, 0);

        var bracket = sssTable.FirstOrDefault(s => monthlyBasicSalary >= s.RangeFrom && monthlyBasicSalary <= s.RangeTo)
            ?? sssTable.LastOrDefault(s => monthlyBasicSalary > s.RangeTo)
            ?? sssTable[0];

        return (bracket.EmployeeShare, bracket.EmployerShare);
    }

    /// <summary>
    /// PhilHealth: 5% total premium on monthly basic salary (50/50 EE/ER split by default),
    /// with statutory floor and ceiling on the contribution basis.
    /// </summary>
    public static (decimal Employee, decimal Employer) ComputePhilHealth(decimal monthlyBasicSalary, PhilHealthConfig? config)
    {
        if (config is null || config.RatePercent <= 0) return (0, 0);

        var basis = Math.Clamp(monthlyBasicSalary, config.MinSalary, config.MaxSalary);
        var premium = Math.Round(basis * config.RatePercent / 100m, 2, MidpointRounding.AwayFromZero);
        var employee = Math.Round(premium * config.EmployeeSharePercent / 100m, 2, MidpointRounding.AwayFromZero);
        var employer = premium - employee;
        return (employee, employer);
    }

    /// <summary>
    /// Pag-IBIG (HDMF): employee 1% if monthly compensation ≤ ₱1,500, otherwise 2%;
    /// employer 2% on compensation capped at ₱10,000 (configurable).
    /// </summary>
    public static (decimal Employee, decimal Employer) ComputePagIbig(decimal monthlyBasicSalary, PagIbigConfig? config)
    {
        if (config is null) return (0, 0);

        var basis = Math.Min(monthlyBasicSalary, config.MaxCompensation);
        var lowThreshold = config.EmployeeLowThreshold > 0 ? config.EmployeeLowThreshold : 1500m;
        var lowRate = config.EmployeeLowRatePercent > 0 ? config.EmployeeLowRatePercent : 1m;
        var eeRate = monthlyBasicSalary <= lowThreshold ? lowRate : config.EmployeeRatePercent;

        var employee = Math.Round(basis * eeRate / 100m, 2, MidpointRounding.AwayFromZero);
        var employer = Math.Round(basis * config.EmployerRatePercent / 100m, 2, MidpointRounding.AwayFromZero);
        return (employee, employer);
    }

    /// <summary>
    /// BIR TRAIN Law monthly withholding tax on taxable compensation
    /// (monthly basic salary minus mandatory SSS, PhilHealth, and Pag-IBIG employee shares).
    /// </summary>
    public static decimal ComputeWithholdingTax(
        decimal monthlyBasicSalary,
        decimal sssEmployee,
        decimal philHealthEmployee,
        decimal pagIbigEmployee,
        IReadOnlyList<TaxBracket> taxTable)
    {
        if (taxTable.Count == 0) return 0;

        var taxable = monthlyBasicSalary - sssEmployee - philHealthEmployee - pagIbigEmployee;
        if (taxable <= 0) return 0;

        var bracket = taxTable.FirstOrDefault(t => taxable >= t.RangeFrom && taxable <= t.RangeTo);
        if (bracket is null || bracket.RatePercentOverExcess <= 0) return 0;

        var excessOver = TrainExcessThreshold(bracket);
        var excess = Math.Max(0, taxable - excessOver);
        return Math.Round(bracket.BaseTax + excess * bracket.RatePercentOverExcess / 100m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>BIR TRAIN monthly table uses fixed "in excess of" thresholds, not bracket RangeFrom.</summary>
    private static decimal TrainExcessThreshold(TaxBracket bracket) => bracket.BaseTax switch
    {
        0 when bracket.RatePercentOverExcess == 15 => 20833m,
        1875m => 33333m,
        8541.80m => 66667m,
        33541.80m => 166667m,
        183541.80m => 666667m,
        _ => bracket.RangeFrom
    };
}
