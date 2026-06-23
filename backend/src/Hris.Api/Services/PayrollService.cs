using System.Text.Json;
using Hris.Api.Data;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Services;

public class PayrollService(HrisDbContext db, AuditService audit, AttendanceSummaryService summaries, PayrollDeductionService deductions)
{
    /// <summary>Computes payslips for all active employees for the given cutoff.</summary>
    public async Task<int> ProcessCutoffAsync(int cutoffId, CancellationToken ct = default)
    {
        var cutoff = await db.PayrollCutoffs.FindAsync(cutoffId)
            ?? throw new InvalidOperationException("Cutoff not found.");
        PayrollCutoffPolicy.ValidateProcess(cutoff);
        if (cutoff.Status is PayrollStatus.Approved or PayrollStatus.Released or PayrollStatus.Closed)
            throw new InvalidOperationException("Cutoff is already finalized.");

        await deductions.InitializeCutoffSelectionsAsync(cutoffId, ct);

        // wipe previous draft results
        var old = await db.Payslips.Where(p => p.PayrollCutoffId == cutoffId).ToListAsync(ct);
        db.Payslips.RemoveRange(old);

        var isFirstHalf = cutoff.PeriodStart.Day <= 15;

        // Executives (VP/CEO) are owners: exempt from timekeeping and payroll.
        var execIds = await ExecutiveExemption.GetExemptEmployeeIdsAsync(db);

        var employees = await db.Employees
            .Where(e => e.Status != EmploymentStatus.Resigned && e.Status != EmploymentStatus.Terminated && e.Status != EmploymentStatus.Retired)
            .Where(e => !execIds.Contains(e.Id))
            .ToListAsync(ct);

        // Solution 3: payroll reads pre-aggregated daily summaries — not raw punch logs.
        await summaries.EnsureRangeAsync(cutoff.PeriodStart, cutoff.PeriodEnd, employees.Select(e => e.Id), ct);

        var dailySummaries = await db.AttendanceDailySummaries
            .Where(s => s.AttendanceDate >= cutoff.PeriodStart && s.AttendanceDate <= cutoff.PeriodEnd)
            .ToListAsync(ct);

        var approvedOt = await db.OvertimeRequests
            .Where(o => o.Status == RequestStatus.Approved && o.OvertimeDate >= cutoff.PeriodStart && o.OvertimeDate <= cutoff.PeriodEnd)
            .ToListAsync(ct);

        var approvedLeaves = await db.LeaveRequests
            .Include(l => l.LeaveType)
            .Where(l => l.Status == RequestStatus.Approved && l.StartDate <= cutoff.PeriodEnd && l.EndDate >= cutoff.PeriodStart)
            .ToListAsync();

        var holidays = await db.Holidays
            .Where(h => h.Date >= cutoff.PeriodStart && h.Date <= cutoff.PeriodEnd)
            .ToListAsync();

        var components = await db.PayComponents
            .Where(c => c.IsActive)
            .ToListAsync();

        var activeLoans = await db.Loans
            .Where(l => l.Status == LoanStatus.Active && l.Balance > 0)
            .ToListAsync();

        // SIL balances — unfiled undertime auto-covered from SIL at payroll release (Regular only).
        var silBalances = await db.LeaveBalances
            .Include(b => b.LeaveType)
            .Where(b => b.Year == cutoff.PeriodStart.Year && b.LeaveType!.Category == LeaveCategory.ServiceIncentive)
            .ToListAsync();

        var year = cutoff.PeriodStart.Year;
        var sssTable = await db.SssBrackets.Where(s => s.EffectiveYear == year).OrderBy(s => s.RangeFrom).ToListAsync();
        if (sssTable.Count == 0) sssTable = await db.SssBrackets.OrderByDescending(s => s.EffectiveYear).ThenBy(s => s.RangeFrom).ToListAsync();
        var phc = await db.PhilHealthConfigs.OrderByDescending(p => p.EffectiveYear).FirstOrDefaultAsync();
        var pic = await db.PagIbigConfigs.OrderByDescending(p => p.EffectiveYear).FirstOrDefaultAsync();
        var taxTable = await db.TaxBrackets.OrderByDescending(t => t.EffectiveYear).ThenBy(t => t.RangeFrom).ToListAsync();

        var deductionTypes = await db.DeductionTypes.Where(t => t.IsActive).ToListAsync(ct);
        var allEmpDeductions = await db.EmployeeDeductions.Include(d => d.DeductionType).Where(d => d.IsActive).ToListAsync(ct);
        var allSelections = await db.PayrollCutoffDeductionSelections.Where(s => s.PayrollCutoffId == cutoffId).ToListAsync(ct);
        var selectionsByEmp = allSelections.GroupBy(s => s.EmployeeId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.EmployeeDeductionId));
        var cutoffUsesChecklist = allSelections.Count > 0;

        var thirteenthMonthEmployeeIds = (await db.EmployeeBenefits
            .Include(eb => eb.Benefit)
            .Where(eb => eb.Benefit!.IsActive && eb.Benefit.IsThirteenthMonth
                && eb.EffectiveDate <= cutoff.PeriodEnd
                && (eb.EndDate == null || eb.EndDate >= cutoff.PeriodStart))
            .Select(eb => eb.EmployeeId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

        var count = 0;
        foreach (var emp in employees)
        {
            var empSel = selectionsByEmp.GetValueOrDefault(emp.Id) ?? new Dictionary<int, PayrollCutoffDeductionSelection>();
            var empHasManaged = allEmpDeductions.Any(d => d.EmployeeId == emp.Id && d.IsActive && d.IsProfileEnabled);
            var slip = ComputePayslip(cutoff, emp, isFirstHalf, dailySummaries, approvedOt, approvedLeaves, holidays, components, activeLoans, sssTable, phc, pic, taxTable, silBalances, deductionTypes, allEmpDeductions, empSel, cutoffUsesChecklist, !empHasManaged, thirteenthMonthEmployeeIds);
            db.Payslips.Add(slip);
            count++;
        }

        cutoff.Status = PayrollStatus.ForApproval;
        cutoff.ProcessedAt = DateTime.UtcNow;
        audit.Log(AuditCategory.Payroll, $"Processed payroll cutoff '{cutoff.Name}' ({count} payslips)", nameof(PayrollCutoff), cutoff.Id.ToString());
        await db.SaveChangesAsync(ct);
        return count;
    }

    private Payslip ComputePayslip(
        PayrollCutoff cutoff, Employee emp, bool isFirstHalf,
        List<AttendanceDailySummary> allSummaries, List<OvertimeRequest> allOt, List<LeaveRequest> allLeaves,
        List<Holiday> holidays, List<PayComponent> allComponents, List<Loan> allLoans,
        List<SssBracket> sssTable, PhilHealthConfig? phc, PagIbigConfig? pic, List<TaxBracket> taxTable,
        List<LeaveBalance> silBalances,
        List<DeductionType> deductionTypes, List<EmployeeDeduction> allEmpDeductions,
        Dictionary<int, PayrollCutoffDeductionSelection> selectionsByDeductionId, bool cutoffUsesChecklist, bool useLegacyLoans,
        HashSet<int> thirteenthMonthEmployeeIds)
    {
        // HR policy: Basic Salary ÷ 24 = daily rate; daily rate ÷ 8 = hourly rate.
        // Basic pay = actual regular hours worked × hourly rate (+ SIL-covered undertime hours).
        var dailyRate = emp.DailyRate ?? Math.Round(emp.MonthlySalary / 24m, 2);
        var hourlyRate = Math.Round(dailyRate / 8m, 4);
        var scheduledHours = ScheduledHoursPerDay(emp);

        var workDays = (emp.WorkDays ?? "Mon,Tue,Wed,Thu,Fri")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var empSummaries = allSummaries.Where(s => s.EmployeeId == emp.Id).ToDictionary(s => s.AttendanceDate);
        var empLeaves = allLeaves.Where(l => l.EmployeeId == emp.Id).ToList();

        decimal daysWorked = 0, daysAbsent = 0, lateMinutes = 0, leaveDaysPaid = 0, undertimeMinutes = 0;
        decimal regularHoursPaid = 0;

        for (var d = cutoff.PeriodStart; d <= cutoff.PeriodEnd; d = d.AddDays(1))
        {
            var dayName = d.ToDateTime(TimeOnly.MinValue).DayOfWeek.ToString()[..3];
            if (!workDays.Contains(dayName)) continue;

            empSummaries.TryGetValue(d, out var day);

            if (day is { Status: "Holiday" } || holidays.Any(h => h.Date == d && h.Type == HolidayType.Regular))
            {
                daysWorked++;
                regularHoursPaid += scheduledHours;
                continue;
            }

            var dayLeave = empLeaves.FirstOrDefault(l => l.StartDate <= d && l.EndDate >= d
                && (l.LeaveType?.IsPaid ?? true)
                && l.LeaveType!.Category is LeaveCategory.Emergency or LeaveCategory.ServiceIncentive);

            if (dayLeave is not null && !dayLeave.IsUndertime)
            {
                if (dayLeave.IsHalfDay)
                {
                    leaveDaysPaid += 0.5m;
                    daysWorked += 0.5m;
                    regularHoursPaid += scheduledHours * 0.5m;
                    if (day is null || !day.HasTimeIn)
                        continue;
                }
                else
                {
                    leaveDaysPaid++;
                    daysWorked++;
                    regularHoursPaid += scheduledHours;
                    continue;
                }
            }

            if (day is { Status: "OnLeave" })
            {
                leaveDaysPaid++;
                daysWorked++;
                regularHoursPaid += scheduledHours;
                continue;
            }

            if (day is null || !day.HasTimeIn)
            {
                daysAbsent++;
                continue;
            }

            daysWorked++;
            lateMinutes += day.LateMinutes;
            undertimeMinutes += day.UndertimeMinutes;

            if (day.HoursWorked > 0)
                regularHoursPaid += day.HoursWorked;
            else
                regularHoursPaid += Math.Max(0, scheduledHours - day.LateMinutes / 60m - day.UndertimeMinutes / 60m);
        }

        // OT policy: only recognized once the employee exceeds 30 minutes beyond regular hours.
        var empOt = allOt.Where(o => o.EmployeeId == emp.Id && o.Hours > 0.5m).ToList();
        var otHours = empOt.Sum(o => o.Hours);
        var otPay = Math.Round(otHours * hourlyRate * 1.25m, 2); // 125% basic OT premium

        var empComponents = allComponents.Where(c => c.EmployeeId == emp.Id &&
            (c.EffectiveFrom == null || c.EffectiveFrom <= cutoff.PeriodEnd) &&
            (c.EffectiveTo == null || c.EffectiveTo >= cutoff.PeriodStart)).ToList();
        var allowances = empComponents.Where(c => c.Type is PayComponentType.Allowance or PayComponentType.Incentive).Sum(c => c.PerCutoff ? c.Amount : c.Amount / 2);
        var bonuses = empComponents.Where(c => c.Type == PayComponentType.Bonus).Sum(c => c.Amount);
        var otherDeductions = cutoff.DeductOtherDeductions
            ? empComponents.Where(c => c.Type == PayComponentType.Deduction).Sum(c => c.PerCutoff ? c.Amount : c.Amount / 2)
            : 0;

        var absenceDeduction = Math.Round(daysAbsent * dailyRate, 2);
        var lateDeduction = Math.Round(lateMinutes / 60m * hourlyRate, 2);

        // Undertime policy:
        // - SIL (filed or auto): restores pay for covered hours; SIL balance charged on approval or release.
        // - EL (filed only): EL balance charged on approval; pay still reduced by undertime hours (no restore).
        // - Uncovered hours (no SIL): deducted from pay at hourly rate.
        var undertimeHours = Math.Round(undertimeMinutes / 60m, 2);
        decimal preFiledSilHours = 0, preFiledElHours = 0;
        for (var d = cutoff.PeriodStart; d <= cutoff.PeriodEnd; d = d.AddDays(1))
        {
            empSummaries.TryGetValue(d, out var daySum);
            var dayUt = daySum?.UndertimeMinutes ?? 0;
            if (dayUt <= 0) continue;
            var dayUtHours = Math.Round(dayUt / 60m, 2);

            var silFiled = empLeaves
                .Where(l => l.IsUndertime && l.LeaveType!.Category == LeaveCategory.ServiceIncentive
                    && l.StartDate <= d && l.EndDate >= d)
                .Sum(l => l.UndertimeHours);
            var elFiled = empLeaves
                .Where(l => l.IsUndertime && l.LeaveType!.Category == LeaveCategory.Emergency
                    && l.StartDate <= d && l.EndDate >= d)
                .Sum(l => l.UndertimeHours);

            var silApplied = Math.Min(silFiled, dayUtHours);
            preFiledSilHours += silApplied;
            preFiledElHours += Math.Min(elFiled, dayUtHours - silApplied);
        }

        var silHoursForPay = preFiledSilHours;
        var remainingForAutoSil = undertimeHours - silHoursForPay;
        decimal autoSilHours = 0;
        if (remainingForAutoSil > 0 && emp.Status == EmploymentStatus.Regular)
        {
            var silBalance = silBalances.FirstOrDefault(b => b.EmployeeId == emp.Id);
            var remainingSilHours = (silBalance is null ? 0 : silBalance.Credits - silBalance.Used) * scheduledHours;
            autoSilHours = Math.Min(remainingForAutoSil, remainingSilHours);
            silHoursForPay += autoSilHours;
        }

        var undertimeSilDays = scheduledHours > 0 ? Math.Round(autoSilHours / scheduledHours, 4) : 0;
        var undertimeElDays = scheduledHours > 0 ? Math.Round(preFiledElHours / scheduledHours, 4) : 0;

        // Pay deduction: all undertime except hours restored by SIL.
        var payDeductUndertimeHours = Math.Round(undertimeHours - silHoursForPay, 4);
        var undertimeDeduction = Math.Round(payDeductUndertimeHours * hourlyRate, 2);

        var earnedBasic = Math.Round((regularHoursPaid + silHoursForPay) * hourlyRate, 2);
        var basicPay = Math.Max(0, earnedBasic);
        var thirteenthMonthPay = thirteenthMonthEmployeeIds.Contains(emp.Id)
            ? Math.Round(basicPay / 12m, 2)
            : 0;
        bonuses += thirteenthMonthPay;
        var grossPay = basicPay + otPay + allowances + bonuses;

        // Government contributions: full monthly amounts deducted on the 2nd cutoff of the month.
        decimal sssEe = 0, sssEr = 0, phEe = 0, phEr = 0, piEe = 0, piEr = 0;
        decimal tax = 0;

        if (emp.UseManualStatutoryContributions)
        {
            if (!isFirstHalf)
            {
                if (cutoff.DeductSss) { sssEe = emp.ManualSssEmployee; sssEr = emp.ManualSssEmployer; }
                if (cutoff.DeductPhilHealth) { phEe = emp.ManualPhilHealthEmployee; phEr = emp.ManualPhilHealthEmployer; }
                if (cutoff.DeductPagIbig) { piEe = emp.ManualPagIbigEmployee; piEr = emp.ManualPagIbigEmployer; }
            }
            if (isFirstHalf && cutoff.DeductTax)
                tax = emp.ManualWithholdingTax;
        }
        else
        {
            var statutory = GovernmentContributionCalculator.ComputeMonthly(
                emp.MonthlySalary, sssTable, phc, pic, taxTable, includeWithholdingTax: false);

            if (!isFirstHalf)
            {
                if (cutoff.DeductSss) { sssEe = statutory.SssEmployee; sssEr = statutory.SssEmployer; }
                if (cutoff.DeductPhilHealth) { phEe = statutory.PhilHealthEmployee; phEr = statutory.PhilHealthEmployer; }
                if (cutoff.DeductPagIbig) { piEe = statutory.PagIbigEmployee; piEr = statutory.PagIbigEmployer; }
            }

            // Withholding tax: full monthly TRAIN amount on the 1st cutoff (15th payroll).
            if (isFirstHalf && cutoff.DeductTax)
                tax = GovernmentContributionCalculator.ComputeWithholdingTax(
                    emp.MonthlySalary, statutory.SssEmployee, statutory.PhilHealthEmployee, statutory.PagIbigEmployee, taxTable);
        }

        decimal loanDeductions = 0;
        var lines = new List<object>();

        if (thirteenthMonthPay > 0)
            lines.Add(new { type = "earning", code = "13TH_MONTH", name = "13th Month Pay (basic ÷ 12)", amount = thirteenthMonthPay });

        var recurringLines = deductions.ComputeForEmployee(
            emp, isFirstHalf, cutoff, deductionTypes, allEmpDeductions, selectionsByDeductionId, allLoans, cutoffUsesChecklist, useLegacyLoans);

        foreach (var dl in recurringLines)
        {
            if (dl.LoanId.HasValue || dl.Code is "LOAN" or "COMPANY_LOAN" or "CASH_ADVANCE" or "SSS_CL" or "SSS_SL" or "HDMF_SR" or "HDMF_CC")
                loanDeductions += dl.Amount;
            else
                otherDeductions += dl.Amount;
            lines.Add(new { type = "deduction", dl.Code, name = dl.Name, amount = dl.Amount, employeeDeductionId = dl.EmployeeDeductionId, loanId = dl.LoanId });
        }

        if (undertimeHours > 0)
        {
            var silPart = silHoursForPay > 0
                ? $" · {silHoursForPay:n2} hr/s covered by SIL (pay restored)"
                : "";
            var elPart = preFiledElHours > 0
                ? $" · {preFiledElHours:n2} hr/s filed EL (credits charged, pay deducted)"
                : "";
            var payPart = payDeductUndertimeHours > 0
                ? $" · {payDeductUndertimeHours:n2} hr/s payroll deduction"
                : "";
            lines.Add(new { type = "undertime", name = $"Undertime {undertimeHours:n2} hr/s{silPart}{elPart}{payPart}", amount = undertimeDeduction });
        }

        var totalDeductions = absenceDeduction + lateDeduction + undertimeDeduction + sssEe + phEe + piEe + tax + loanDeductions + otherDeductions;
        var netPay = Math.Round(grossPay - (sssEe + phEe + piEe + tax + loanDeductions + otherDeductions), 2);

        return new Payslip
        {
            PayrollCutoffId = cutoff.Id,
            EmployeeId = emp.Id,
            BasicPay = basicPay,
            OvertimePay = otPay,
            Allowances = allowances,
            Bonuses = bonuses,
            GrossPay = grossPay,
            AbsenceDeduction = absenceDeduction,
            LateDeduction = lateDeduction,
            SssEmployee = sssEe, SssEmployer = sssEr,
            PhilHealthEmployee = phEe, PhilHealthEmployer = phEr,
            PagIbigEmployee = piEe, PagIbigEmployer = piEr,
            WithholdingTax = tax,
            LoanDeductions = loanDeductions,
            OtherDeductions = otherDeductions,
            TotalDeductions = totalDeductions,
            NetPay = netPay,
            DaysWorked = daysWorked,
            DaysAbsent = daysAbsent,
            LateMinutes = Math.Round(lateMinutes, 0),
            OvertimeHours = otHours,
            LeaveDaysPaid = leaveDaysPaid,
            UndertimeHours = undertimeHours,
            UndertimeLeaveDays = undertimeSilDays,
            UndertimeElDays = undertimeElDays,
            UndertimeDeduction = undertimeDeduction,
            DetailsJson = JsonSerializer.Serialize(lines)
        };
    }

    /// <summary>Marks cutoff released and posts loan payments.</summary>
    public async Task ReleaseCutoffAsync(int cutoffId)
    {
        var cutoff = await db.PayrollCutoffs.Include(c => c.Payslips).FirstOrDefaultAsync(c => c.Id == cutoffId)
            ?? throw new InvalidOperationException("Cutoff not found.");
        if (cutoff.Status != PayrollStatus.Approved)
            throw new InvalidOperationException("Cutoff must be approved before release.");

        PayrollCutoffPolicy.ValidateRelease(cutoff);

        var isFirstHalf = cutoff.PeriodStart.Day <= 15;
        var useManaged = await db.PayrollCutoffDeductionSelections.AnyAsync(s => s.PayrollCutoffId == cutoffId);
        var loans = await db.Loans.Where(l => l.Status == LoanStatus.Active).ToListAsync();

        if (useManaged)
        {
            foreach (var slip in cutoff.Payslips)
            {
                if (string.IsNullOrEmpty(slip.DetailsJson)) continue;
                var parsed = JsonSerializer.Deserialize<List<JsonElement>>(slip.DetailsJson) ?? [];
                var dedLines = new List<DeductionLine>();
                foreach (var el in parsed)
                {
                    if (el.TryGetProperty("type", out var t) && t.GetString() == "deduction")
                    {
                        dedLines.Add(new DeductionLine(
                            el.GetProperty("code").GetString() ?? "",
                            el.GetProperty("name").GetString() ?? "",
                            el.GetProperty("amount").GetDecimal(),
                            el.TryGetProperty("employeeDeductionId", out var eid) && eid.ValueKind != JsonValueKind.Null ? eid.GetInt32() : null,
                            el.TryGetProperty("loanId", out var lid) && lid.ValueKind != JsonValueKind.Null ? lid.GetInt32() : null));
                    }
                }
                if (dedLines.Count > 0)
                    await deductions.PostInstallmentPaymentsAsync(cutoff, slip, dedLines);
            }
        }
        else
        {
            foreach (var slip in cutoff.Payslips.Where(p => p.LoanDeductions > 0))
            {
                foreach (var loan in loans.Where(l => l.EmployeeId == slip.EmployeeId && l.Balance > 0))
                {
                    if (loan.Type is LoanType.SssLoan or LoanType.PagIbigLoan && !isFirstHalf) continue;
                    var amount = Math.Min(loan.AmortizationPerCutoff, loan.Balance);
                    if (amount <= 0) continue;
                    loan.Balance -= amount;
                    if (loan.Balance <= 0) loan.Status = LoanStatus.FullyPaid;
                    db.LoanPayments.Add(new LoanPayment { LoanId = loan.Id, PayslipId = slip.Id, PaymentDate = cutoff.PayDate, Amount = amount, Remarks = $"Payroll deduction - {cutoff.Name}" });
                }
            }
        }

        // Post auto undertime charges against SIL balances on release (Regular employees only).
        // EL undertime credits are charged when the leave request is approved — not at release.
        var year = cutoff.PeriodStart.Year;
        var chargedSilSlips = cutoff.Payslips.Where(p => p.UndertimeLeaveDays > 0).ToList();
        if (chargedSilSlips.Count > 0)
        {
            var silBalances = await db.LeaveBalances
                .Include(b => b.LeaveType)
                .Where(b => b.Year == year && b.LeaveType!.Category == LeaveCategory.ServiceIncentive)
                .ToListAsync();
            foreach (var slip in chargedSilSlips)
            {
                var emp = await db.Employees.FindAsync(slip.EmployeeId);
                if (emp?.Status != EmploymentStatus.Regular) continue;
                var balance = silBalances.FirstOrDefault(b => b.EmployeeId == slip.EmployeeId);
                if (balance is not null)
                    balance.Used = Math.Min(balance.Credits, balance.Used + slip.UndertimeLeaveDays);
            }
        }

        // Year-end SIL cash conversion: unused SIL credits paid out on the December 2nd cutoff.
        if (cutoff.PeriodEnd.Month == 12 && cutoff.PeriodStart.Day > 15)
            await ApplySilCashConversionAsync(cutoff);

        cutoff.Status = PayrollStatus.Released;
        audit.Log(AuditCategory.Payroll, $"Released payroll cutoff '{cutoff.Name}'", nameof(PayrollCutoff), cutoff.Id.ToString());
        await db.SaveChangesAsync();
    }

    /// <summary>Expected paid hours per shift day (shift length minus break).</summary>
    private static decimal ScheduledHoursPerDay(Employee emp)
    {
        var span = emp.ShiftEnd.ToTimeSpan() - emp.ShiftStart.ToTimeSpan();
        if (span <= TimeSpan.Zero) span = TimeSpan.FromHours(8);
        return Math.Round(Math.Max(0, (decimal)span.TotalHours - emp.BreakMinutes / 60m), 2);
    }

    /// <summary>Unused SIL credits converted to cash (daily rate per day) on year-end release.</summary>
    private async Task ApplySilCashConversionAsync(PayrollCutoff cutoff)
    {
        var year = cutoff.PeriodStart.Year;
        var silType = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Category == LeaveCategory.ServiceIncentive && t.IsActive);
        if (silType is null || !silType.IsConvertibleToCash) return;

        var balances = await db.LeaveBalances
            .Where(b => b.Year == year && b.LeaveTypeId == silType.Id)
            .ToListAsync();
        var employees = await db.Employees
            .Where(e => e.Status == EmploymentStatus.Regular)
            .ToDictionaryAsync(e => e.Id);

        foreach (var slip in cutoff.Payslips)
        {
            if (!employees.TryGetValue(slip.EmployeeId, out var emp)) continue;
            var bal = balances.FirstOrDefault(b => b.EmployeeId == slip.EmployeeId);
            if (bal is null) continue;
            var remaining = bal.Credits - bal.Used;
            if (remaining <= 0) continue;

            var dailyRate = emp.DailyRate ?? Math.Round(emp.MonthlySalary / 24m, 2);
            var cashOut = Math.Round(remaining * dailyRate, 2);
            slip.Bonuses += cashOut;
            slip.GrossPay += cashOut;
            slip.NetPay += cashOut;
            bal.Used = bal.Credits;
        }
    }
}
