using Hris.Domain;
using Hris.Domain.Entities;

namespace Hris.Api.Services;

/// <summary>
/// Semi-monthly payroll rules: 1–15 paid on the 20th, 16–EOM paid on the 5th of the following month.
/// Processing requires a completed period; release is allowed only within 5 days before pay day through pay day.
/// </summary>
public static class PayrollCutoffPolicy
{
    public const int ReleaseDaysBeforePayDate = 5;

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    public static bool IsFirstHalf(DateOnly periodStart) => periodStart.Day <= 15;

    public static DateOnly StandardPayDate(DateOnly periodStart, DateOnly periodEnd)
    {
        if (IsFirstHalf(periodStart))
            return new DateOnly(periodStart.Year, periodStart.Month, 20);

        var payMonth = periodEnd.Month == 12 ? 1 : periodEnd.Month + 1;
        var payYear = periodEnd.Month == 12 ? periodEnd.Year + 1 : periodEnd.Year;
        return new DateOnly(payYear, payMonth, 5);
    }

    public static DateOnly ReleaseWindowStart(DateOnly payDate) =>
        payDate.AddDays(-ReleaseDaysBeforePayDate);

    public static void ValidateCreate(PayrollCutoff cutoff, IEnumerable<PayrollCutoff> existing)
    {
        if (cutoff.PeriodStart > cutoff.PeriodEnd)
            throw new InvalidOperationException("Period start must be on or before period end.");

        var today = Today();
        if (cutoff.PeriodStart > today)
            throw new InvalidOperationException("Cannot create a cutoff for a period that has not started yet.");

        if (cutoff.PeriodEnd > today)
            throw new InvalidOperationException(
                $"Cannot create a cutoff until the period ends ({cutoff.PeriodEnd:MMM d, yyyy}). Today is {today:MMM d, yyyy}.");

        ValidateSemiMonthlyShape(cutoff.PeriodStart, cutoff.PeriodEnd);

        var expectedPay = StandardPayDate(cutoff.PeriodStart, cutoff.PeriodEnd);
        if (cutoff.PayDate == default)
            cutoff.PayDate = expectedPay;
        else if (cutoff.PayDate != expectedPay)
            throw new InvalidOperationException(
                $"Pay date must be {expectedPay:yyyy-MM-dd} for this period (1–15 → 20th, 16–EOM → 5th of next month).");

        if (existing.Any(c =>
                c.PeriodStart == cutoff.PeriodStart && c.PeriodEnd == cutoff.PeriodEnd))
            throw new InvalidOperationException("A cutoff for this period already exists.");
    }

    public static void ValidateProcess(PayrollCutoff cutoff)
    {
        var today = Today();
        if (cutoff.PeriodEnd > today)
            throw new InvalidOperationException(
                $"Cannot process payroll until the cutoff period ends ({cutoff.PeriodEnd:MMM d, yyyy}).");
    }

    public static void ValidateRelease(PayrollCutoff cutoff)
    {
        var today = Today();
        if (cutoff.PeriodEnd > today)
            throw new InvalidOperationException(
                $"Cannot release payroll until the cutoff period ends ({cutoff.PeriodEnd:MMM d, yyyy}).");

        var windowStart = ReleaseWindowStart(cutoff.PayDate);
        if (today < windowStart)
            throw new InvalidOperationException(
                $"Release opens on {windowStart:MMM d, yyyy} (within {ReleaseDaysBeforePayDate} days of pay day {cutoff.PayDate:MMM d, yyyy}).");

        if (today > cutoff.PayDate)
            throw new InvalidOperationException(
                $"Pay day ({cutoff.PayDate:MMM d, yyyy}) has passed. Contact an administrator if this cutoff still needs release.");
    }

    public static (bool CanProcess, bool CanRelease, bool CanReset, DateOnly? ReleaseAvailableOn, string? BlockReason) GetAvailability(PayrollCutoff cutoff)
    {
        var today = Today();
        var windowStart = ReleaseWindowStart(cutoff.PayDate);

        if (cutoff.PeriodEnd > today)
        {
            var resetAllowed = cutoff.Status is not (PayrollStatus.Released or PayrollStatus.Closed);
            return (false, false, resetAllowed, windowStart,
                $"Period ends {cutoff.PeriodEnd:MMM d, yyyy}. Process and release unlock after that date.");
        }

        var canProcess = cutoff.Status == PayrollStatus.Draft;
        var canRelease = cutoff.Status == PayrollStatus.Approved
                         && today >= windowStart
                         && today <= cutoff.PayDate;
        var canReset = cutoff.Status is PayrollStatus.ForApproval or PayrollStatus.ForCeoApproval or PayrollStatus.Approved
                       && !canRelease
                       && cutoff.Status is not (PayrollStatus.Released or PayrollStatus.Closed);

        string? blockReason = null;
        if (cutoff.Status == PayrollStatus.Approved && !canRelease)
        {
            if (today < windowStart)
                blockReason = $"Release available from {windowStart:MMM d, yyyy} (pay day {cutoff.PayDate:MMM d, yyyy}).";
            else if (today > cutoff.PayDate)
                blockReason = $"Pay day ({cutoff.PayDate:MMM d, yyyy}) has passed.";
        }
        else if (cutoff.Status == PayrollStatus.Draft && cutoff.PeriodEnd > today)
        {
            blockReason = $"Period ends {cutoff.PeriodEnd:MMM d, yyyy}.";
        }

        return (canProcess, canRelease, canReset, windowStart, blockReason);
    }

    private static void ValidateSemiMonthlyShape(DateOnly start, DateOnly end)
    {
        if (IsFirstHalf(start))
        {
            if (start.Day != 1 || end.Day != 15 || start.Month != end.Month || start.Year != end.Year)
                throw new InvalidOperationException("First-half cutoffs must run from the 1st through the 15th of the same month.");
            return;
        }

        if (start.Day != 16)
            throw new InvalidOperationException("Second-half cutoffs must start on the 16th.");

        var lastDay = DateTime.DaysInMonth(end.Year, end.Month);
        if (end.Day != lastDay || start.Month != end.Month || start.Year != end.Year)
            throw new InvalidOperationException("Second-half cutoffs must run from the 16th through the last day of the month.");
    }
}
