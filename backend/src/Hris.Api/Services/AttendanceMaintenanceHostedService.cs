using Hris.Api.Data;
using Hris.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Services;

/// <summary>
/// Solution 9: moves attendance logs past the retention window into AttendanceLogArchives.
/// Also refreshes yesterday's daily summaries nightly.
/// </summary>
public class AttendanceMaintenanceHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<AttendanceMaintenanceHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial backfill shortly after startup.
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        await RunMaintenanceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            await RunMaintenanceAsync(stoppingToken);
        }
    }

    private async Task RunMaintenanceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HrisDbContext>();
        var summaries = scope.ServiceProvider.GetRequiredService<AttendanceSummaryService>();

        var retainYears = config.GetValue("Performance:AttendanceRetainYears", 3);
        var archiveBefore = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-retainYears));

        logger.LogInformation("Attendance maintenance: archiving logs before {Date}.", archiveBefore);

        var batchSize = config.GetValue("Performance:ArchiveBatchSize", 2000);
        var toArchive = await db.AttendanceLogs
            .Where(l => l.AttendanceDate < archiveBefore)
            .OrderBy(l => l.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        if (toArchive.Count > 0)
        {
            foreach (var log in toArchive)
            {
                db.AttendanceLogArchives.Add(new AttendanceLogArchive
                {
                    SyncGuid = log.SyncGuid,
                    EmployeeId = log.EmployeeId,
                    SiteId = log.SiteId,
                    DeviceId = log.DeviceId,
                    PunchTime = log.PunchTime,
                    AttendanceDate = log.AttendanceDate == default ? DateOnly.FromDateTime(log.PunchTime) : log.AttendanceDate,
                    AttendanceYear = log.AttendanceYear == 0 ? log.PunchTime.Year : log.AttendanceYear,
                    PunchType = log.PunchType,
                    Source = log.Source,
                    VerifyMode = log.VerifyMode,
                    IsCorrected = log.IsCorrected,
                    Remarks = log.Remarks,
                    SyncedAt = log.SyncedAt
                });
            }
            db.AttendanceLogs.RemoveRange(toArchive);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Archived {Count} attendance log(s).", toArchive.Count);
        }

        // Refresh yesterday's summaries (covers late syncs).
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        await summaries.EnsureRangeAsync(yesterday, yesterday, ct: ct);

        // One-time / incremental backfill for legacy data.
        await summaries.BackfillAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Attendance maintenance skipped this cycle.");
        }
    }
}
