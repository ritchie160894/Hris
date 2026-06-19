namespace Hris.Api.Services;

/// <summary>Removes sync batch history past the admin-configured retention window.</summary>
public class SyncBatchMaintenanceHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<SyncBatchMaintenanceHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        await RunCleanupAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            await RunCleanupAsync(stoppingToken);
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var maintenance = scope.ServiceProvider.GetRequiredService<SyncBatchMaintenanceService>();
            await maintenance.PurgeExpiredBatchesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sync batch cleanup skipped this cycle.");
        }
    }
}
