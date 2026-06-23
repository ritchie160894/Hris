namespace Hris.Api.Services;

/// <summary>Removes in-app notifications past the configured retention window (default 7 days).</summary>
public class NotificationCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationCleanupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
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
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
            var deleted = await notifications.PurgeExpiredAsync(ct);
            if (deleted > 0)
                logger.LogInformation("Notification cleanup removed {Count} expired notification(s).", deleted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Notification cleanup skipped this cycle.");
        }
    }
}
