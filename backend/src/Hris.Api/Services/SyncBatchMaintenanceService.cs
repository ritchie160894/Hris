using Hris.Api.Data;
using Hris.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Services;

public class SyncBatchMaintenanceService(HrisDbContext db, IConfiguration config, ILogger<SyncBatchMaintenanceService> logger)
{
    public const string RetentionSettingKey = "SyncBatchRetentionDays";
    public static readonly int[] AllowedRetentionDays = [7, 30, 90, 0];

    public async Task<int> GetRetentionDaysAsync(CancellationToken ct = default)
    {
        var setting = await db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == RetentionSettingKey, ct);
        if (setting != null && int.TryParse(setting.Value, out var days) && AllowedRetentionDays.Contains(days))
            return days;
        return config.GetValue("Sync:BatchRetentionDays", 30);
    }

    public async Task SetRetentionDaysAsync(int days, CancellationToken ct = default)
    {
        if (!AllowedRetentionDays.Contains(days))
            throw new ArgumentOutOfRangeException(nameof(days), "Retention must be 7, 30, 90, or 0 (keep all).");

        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == RetentionSettingKey, ct);
        if (setting is null)
        {
            db.SystemSettings.Add(new SystemSetting { Key = RetentionSettingKey, Value = days.ToString() });
        }
        else
        {
            setting.Value = days.ToString();
            setting.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes sync batches older than the configured retention window. Returns rows removed.</summary>
    public async Task<int> PurgeExpiredBatchesAsync(CancellationToken ct = default)
    {
        var retentionDays = await GetRetentionDaysAsync(ct);
        if (retentionDays <= 0) return 0;

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var batchSize = config.GetValue("Sync:CleanupBatchSize", 1000);
        var totalRemoved = 0;

        while (!ct.IsCancellationRequested)
        {
            var ids = await db.SyncBatches
                .Where(b => b.CreatedAt < cutoff)
                .OrderBy(b => b.Id)
                .Take(batchSize)
                .Select(b => b.Id)
                .ToListAsync(ct);
            if (ids.Count == 0) break;

            var removed = await db.SyncBatches.Where(b => ids.Contains(b.Id)).ExecuteDeleteAsync(ct);
            totalRemoved += removed;
            if (ids.Count < batchSize) break;
        }

        if (totalRemoved > 0)
            logger.LogInformation("Purged {Count} sync batch(es) older than {Days} day(s).", totalRemoved, retentionDays);

        return totalRemoved;
    }

    public static string RetentionLabel(int days) => days switch
    {
        7 => "1 week",
        30 => "1 month",
        90 => "3 months",
        0 => "Keep all",
        _ => $"{days} days"
    };
}
