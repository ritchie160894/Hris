using Hris.Api.Data;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Services;

public class NotificationService(HrisDbContext db, IConfiguration config)
{
    public void Notify(int userId, NotificationType type, string title, string message, string? link = null)
    {
        db.Notifications.Add(new Notification
        {
            UserId = userId, Type = type, Title = title, Message = message, LinkUrl = link,
            DeliveryStatus = NotificationDeliveryStatus.Delivered, DeliveredAt = DateTime.UtcNow
        });
    }

    /// <summary>Notify all active users holding a role (optionally scoped to a department for Department Heads).</summary>
    public async Task NotifyRoleAsync(UserRole role, NotificationType type, string title, string message, int? departmentId = null, string? link = null)
    {
        var query = db.Users.Where(u => u.IsActive && u.Role == role);
        if (departmentId.HasValue && role == UserRole.DepartmentHead)
            query = query.Where(u => u.Employee != null && u.Employee.DepartmentId == departmentId);
        var userIds = await query.Select(u => u.Id).ToListAsync();
        foreach (var id in userIds) Notify(id, type, title, message, link);
    }

    public async Task NotifyEmployeeAsync(int employeeId, NotificationType type, string title, string message, string? link = null)
    {
        var userIds = await db.Users.Where(u => u.IsActive && u.EmployeeId == employeeId).Select(u => u.Id).ToListAsync();
        foreach (var id in userIds) Notify(id, type, title, message, link);
    }

    /// <summary>Deletes in-app notifications older than the configured retention window.</summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        var retainDays = config.GetValue("Notifications:RetainDays", 7);
        if (retainDays < 1) retainDays = 1;
        var cutoff = DateTime.UtcNow.AddDays(-retainDays);
        return await db.Notifications.Where(n => n.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
    }
}
