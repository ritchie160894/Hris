using Hris.Api.Data;
using Hris.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Services;

/// <summary>Solution 4: payroll runs in the background — the API returns immediately.</summary>
public class PayrollBackgroundService(IServiceScopeFactory scopeFactory, ILogger<PayrollBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextQueuedCutoffAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Payroll background worker error.");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    private async Task ProcessNextQueuedCutoffAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HrisDbContext>();
        var payroll = scope.ServiceProvider.GetRequiredService<PayrollService>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var dashboard = scope.ServiceProvider.GetRequiredService<DashboardService>();

        var cutoff = await db.PayrollCutoffs
            .Where(c => c.Status == PayrollStatus.Processing)
            .OrderBy(c => c.UpdatedAt ?? c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (cutoff is null) return;

        logger.LogInformation("Processing payroll cutoff #{Id} ({Name}) in background.", cutoff.Id, cutoff.Name);

        try
        {
            var count = await payroll.ProcessCutoffAsync(cutoff.Id, ct);
            await notifications.NotifyRoleAsync(UserRole.VicePresidentHrHead, NotificationType.Payroll,
                "Payroll awaiting approval", $"Payroll cutoff '{cutoff.Name}' ({count} payslips) requires approval.");
            await db.SaveChangesAsync(ct);
            dashboard.Invalidate();
            logger.LogInformation("Payroll cutoff #{Id} completed ({Count} payslips).", cutoff.Id, count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Payroll cutoff #{Id} failed.", cutoff.Id);
            cutoff.Status = PayrollStatus.Draft;
            cutoff.ProcessingError = ex.Message;
            await db.SaveChangesAsync(ct);
        }
    }
}
