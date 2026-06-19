using Hris.Api.Data;
using Hris.Domain;
using Hris.Domain.Entities;

namespace Hris.Api.Services;

public class AuditService(HrisDbContext db, IHttpContextAccessor http)
{
    public void Log(AuditCategory category, string action, string? entityType = null, string? entityId = null, string? details = null)
    {
        var ctx = http.HttpContext;
        int? userId = null;
        string? userName = null;
        if (ctx?.User?.Identity?.IsAuthenticated == true)
        {
            userName = ctx.User.Identity.Name;
            var idClaim = ctx.User.FindFirst("uid")?.Value;
            if (int.TryParse(idClaim, out var uid)) userId = uid;
        }
        db.AuditLogs.Add(new AuditLog
        {
            Category = category,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            UserId = userId,
            UserName = userName,
            IpAddress = ctx?.Connection?.RemoteIpAddress?.ToString()
        });
    }
}
