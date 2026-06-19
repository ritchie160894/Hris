using Hris.Api.Data;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Services;

/// <summary>
/// Maintains the AttendanceDailySummary table (Solution 3).
/// Payroll and monitoring read summaries — not millions of raw punch rows.
/// </summary>
public class AttendanceSummaryService(HrisDbContext db)
{
    public static void StampLog(AttendanceLog log)
    {
        log.AttendanceDate = DateOnly.FromDateTime(log.PunchTime);
        log.AttendanceYear = log.PunchTime.Year;
    }

    /// <summary>Recompute one employee-day summary from raw logs.</summary>
    public async Task UpsertDailyAsync(int employeeId, DateOnly date, CancellationToken ct = default)
    {
        var emp = await db.Employees.FindAsync([employeeId], ct);
        if (emp is null) return;

        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = date.ToDateTime(TimeOnly.MaxValue);
        var logs = await db.AttendanceLogs
            .Where(l => l.EmployeeId == employeeId && l.AttendanceDate == date)
            .OrderBy(l => l.PunchTime)
            .ToListAsync(ct);

        var onLeave = await db.LeaveRequests.AnyAsync(l =>
            l.EmployeeId == employeeId && l.Status == RequestStatus.Approved &&
            l.StartDate <= date && l.EndDate >= date && !l.IsUndertime && !l.IsHalfDay, ct);

        var holiday = await db.Holidays.FirstOrDefaultAsync(h => h.Date == date, ct);
        var workDays = (emp.WorkDays ?? "Mon,Tue,Wed,Thu,Fri")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dayName = date.ToDateTime(TimeOnly.MinValue).DayOfWeek.ToString()[..3];
        var isWorkDay = workDays.Contains(dayName);

        var timeIn = logs.FirstOrDefault(l => l.PunchType == PunchType.TimeIn)?.PunchTime;
        var timeOut = logs.LastOrDefault(l => l.PunchType == PunchType.TimeOut)?.PunchTime;
        var breakOut = logs.FirstOrDefault(l => l.PunchType == PunchType.BreakOut)?.PunchTime;
        var breakIn = logs.FirstOrDefault(l => l.PunchType == PunchType.BreakIn)?.PunchTime;

        decimal lateMinutes = 0, undertimeMinutes = 0, hoursWorked = 0;
        string status;

        if (!isWorkDay)
            status = "RestDay";
        else if (holiday is { Type: HolidayType.Regular })
            status = "Holiday";
        else if (onLeave)
            status = "OnLeave";
        else if (timeIn is null)
            status = "Absent";
        else
        {
            var shiftStart = date.ToDateTime(emp.ShiftStart);
            if (timeIn > shiftStart.AddMinutes(5))
                lateMinutes = (decimal)(timeIn.Value - shiftStart).TotalMinutes;

            if (timeOut is not null)
            {
                hoursWorked = Math.Round((decimal)(timeOut.Value - timeIn.Value).TotalHours - emp.BreakMinutes / 60m, 2);
                var shiftEnd = date.ToDateTime(emp.ShiftEnd);
                if (timeOut < shiftEnd)
                    undertimeMinutes = (decimal)(shiftEnd - timeOut.Value).TotalMinutes;
            }

            status = lateMinutes > 0 ? "Late" : "Present";
        }

        var summary = await db.AttendanceDailySummaries
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId && s.AttendanceDate == date, ct);

        if (summary is null)
        {
            summary = new AttendanceDailySummary { EmployeeId = employeeId, AttendanceDate = date };
            db.AttendanceDailySummaries.Add(summary);
        }

        summary.AttendanceYear = date.Year;
        summary.TimeIn = timeIn;
        summary.TimeOut = timeOut;
        summary.BreakOut = breakOut;
        summary.BreakIn = breakIn;
        summary.HoursWorked = Math.Max(0, hoursWorked);
        summary.LateMinutes = Math.Round(lateMinutes, 0);
        summary.UndertimeMinutes = Math.Round(undertimeMinutes, 0);
        summary.Status = status;
        summary.HasTimeIn = timeIn is not null;
        summary.ComputedAt = DateTime.UtcNow;
    }

    public async Task UpsertForLogAsync(AttendanceLog log, CancellationToken ct = default)
    {
        StampLog(log);
        await UpsertDailyAsync(log.EmployeeId, log.AttendanceDate, ct);
    }

    /// <summary>Ensure summaries exist for every day in a payroll period.</summary>
    public async Task EnsureRangeAsync(DateOnly from, DateOnly to, IEnumerable<int>? employeeIds = null, CancellationToken ct = default)
    {
        var ids = employeeIds?.ToList();
        if (ids is null || ids.Count == 0)
        {
            var execIds = await ExecutiveExemption.GetExemptEmployeeIdsAsync(db);
            ids = await db.Employees
                .Where(e => e.Status != EmploymentStatus.Resigned && e.Status != EmploymentStatus.Terminated && e.Status != EmploymentStatus.Retired)
                .Where(e => !execIds.Contains(e.Id))
                .Select(e => e.Id)
                .ToListAsync(ct);
        }

        for (var d = from; d <= to; d = d.AddDays(1))
        foreach (var id in ids)
            await UpsertDailyAsync(id, d, ct);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Backfill AttendanceDate/Year on legacy logs and rebuild summaries.</summary>
    public async Task BackfillAsync(CancellationToken ct = default)
    {
        var unstamped = await db.AttendanceLogs.Where(l => l.AttendanceDate == default).Take(5000).ToListAsync(ct);
        foreach (var log in unstamped)
            StampLog(log);

        if (unstamped.Count > 0)
            await db.SaveChangesAsync(ct);

        var minDate = await db.AttendanceLogs.MinAsync(l => (DateOnly?)l.AttendanceDate, ct);
        var maxDate = await db.AttendanceLogs.MaxAsync(l => (DateOnly?)l.AttendanceDate, ct);
        if (minDate is null || maxDate is null) return;

        await EnsureRangeAsync(minDate.Value, maxDate.Value, ct: ct);
    }
}
