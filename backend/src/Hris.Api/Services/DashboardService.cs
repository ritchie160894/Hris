using Hris.Api.Data;
using Hris.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Hris.Api.Services;

/// <summary>Solution 7: cache expensive dashboard aggregates in memory.</summary>
public class DashboardService(HrisDbContext db, IMemoryCache cache, IConfiguration config)
{
    private int CacheSeconds => config.GetValue("Performance:DashboardCacheSeconds", 60);

    public async Task<object> GetStatsAsync(int userId, CancellationToken ct = default)
    {
        var cacheKey = "dashboard:global";
        if (cache.TryGetValue(cacheKey, out object? cached) && cached is not null)
            return cached;

        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var activeStatuses = new[] { EmploymentStatus.Probationary, EmploymentStatus.Regular, EmploymentStatus.Contractual, EmploymentStatus.ProjectBased, EmploymentStatus.OnLeave };
        var execIds = await ExecutiveExemption.GetExemptEmployeeIdsAsync(db);

        // Prefer daily summaries for present-today (indexed, one row per employee).
        var todayDate = DateOnly.FromDateTime(today);
        var presentToday = await db.AttendanceDailySummaries
            .Where(s => s.AttendanceDate == todayDate && s.HasTimeIn && !execIds.Contains(s.EmployeeId))
            .CountAsync(ct);

        var totalEmployees = await db.Employees.CountAsync(e => activeStatuses.Contains(e.Status) && !execIds.Contains(e.Id), ct);

        var onLeaveToday = await db.LeaveRequests.CountAsync(l =>
            l.Status == RequestStatus.Approved &&
            l.StartDate <= todayDate && l.EndDate >= todayDate, ct);

        var pendingLeaves = await db.LeaveRequests.CountAsync(l => l.Status == RequestStatus.Pending || l.Status == RequestStatus.InProgress, ct);
        var pendingOt = await db.OvertimeRequests.CountAsync(o => o.Status == RequestStatus.Pending || o.Status == RequestStatus.InProgress, ct);
        var pendingLoans = await db.Loans.CountAsync(l => l.ApprovalStatus == RequestStatus.Pending || l.ApprovalStatus == RequestStatus.InProgress, ct);
        var pendingCorrections = await db.AttendanceCorrections.CountAsync(c => c.Status == RequestStatus.Pending || c.Status == RequestStatus.InProgress, ct);

        var devices = await db.BiometricDevices.Where(d => d.IsActive)
            .GroupBy(d => d.Status)
            .Select(g => new { status = g.Key.ToString(), count = g.Count() })
            .ToListAsync(ct);

        var sites = await db.Sites.Include(s => s.Branch).Where(s => s.IsActive)
            .Select(s => new
            {
                s.Id, s.Name, branch = s.Branch!.Name,
                s.LastHeartbeatAt, s.LastSyncAt, s.PendingSyncCount,
                online = s.LastHeartbeatAt != null && s.LastHeartbeatAt > DateTime.UtcNow.AddMinutes(-10)
            }).ToListAsync(ct);

        var latestCutoff = await db.PayrollCutoffs.OrderByDescending(c => c.PeriodEnd)
            .Select(c => new { c.Id, c.Name, status = c.Status.ToString(), c.PayDate })
            .FirstOrDefaultAsync(ct);

        var payrollMonthTotal = await db.Payslips
            .Where(p => p.PayrollCutoff!.PeriodStart >= monthStart)
            .SumAsync(p => (decimal?)p.NetPay, ct) ?? 0;

        var byDepartment = await db.Employees
            .Where(e => activeStatuses.Contains(e.Status) && e.DepartmentId != null && !execIds.Contains(e.Id))
            .GroupBy(e => e.Department!.Name)
            .Select(g => new { department = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var byBranch = await db.Employees
            .Where(e => activeStatuses.Contains(e.Status) && e.BranchId != null && !execIds.Contains(e.Id))
            .GroupBy(e => e.Branch!.Name)
            .Select(g => new { branch = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var recentAnnouncements = await db.Announcements.Where(a => a.IsActive)
            .OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.PublishDate)
            .Take(5)
            .Select(a => new { a.Id, a.Title, type = a.Type.ToString(), a.PublishDate, a.IsPinned })
            .ToListAsync(ct);

        var unreadNotifications = await db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);

        var result = new
        {
            employees = new { total = totalEmployees, presentToday, onLeaveToday, absentToday = Math.Max(0, totalEmployees - presentToday - onLeaveToday) },
            pendingApprovals = new { leaves = pendingLeaves, overtime = pendingOt, loans = pendingLoans, corrections = pendingCorrections, total = pendingLeaves + pendingOt + pendingLoans + pendingCorrections },
            devices,
            sites,
            payroll = new { latestCutoff, monthToDateNet = payrollMonthTotal },
            byDepartment,
            byBranch,
            recentAnnouncements,
            unreadNotifications
        };

        cache.Set(cacheKey, result, TimeSpan.FromSeconds(CacheSeconds));
        return result;
    }

    public void Invalidate() => cache.Remove("dashboard:global");
}
