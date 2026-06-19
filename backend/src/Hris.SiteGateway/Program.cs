using Hris.SiteGateway;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Local database: SQL Server Express ("Provider": "SqlServer") or zero-config SQLite (default).
var provider = builder.Configuration["LocalDb:Provider"] ?? "Sqlite";
builder.Services.AddDbContext<LocalDbContext>(o =>
{
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        o.UseSqlServer(builder.Configuration.GetConnectionString("Local"));
    else
        o.UseSqlite(builder.Configuration.GetConnectionString("Local") ?? "Data Source=sitegateway.db");
});

builder.Services.AddHttpClient("central", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Central:BaseUrl"] ?? "http://localhost:5000/");
    client.DefaultRequestHeaders.Add("X-Site-Key", builder.Configuration["Central:SiteApiKey"] ?? "");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHostedService<SyncWorker>();
builder.Services.AddWindowsService(o => o.ServiceName = "HRIS Site Gateway");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
    db.Database.EnsureCreated();
    await LocalGatewayBootstrap.ApplyAsync(db, app.Logger);
}

app.MapGet("/", () => Results.Json(new { service = "HRIS Site Gateway", status = "running", offlineCapable = true }));

// Local health/status endpoint for technicians on site (works without central internet).
app.MapGet("/status", async (LocalDbContext db) =>
{
    var pending = await db.Attendance.CountAsync(a => !a.Synced && !a.PermanentFailure);
    var failed = await db.Attendance.CountAsync(a => a.PermanentFailure);
    var total = await db.Attendance.CountAsync();
    var employees = await db.Employees.CountAsync();
    var oldestPending = await db.Attendance
        .Where(a => !a.Synced && !a.PermanentFailure)
        .OrderBy(a => a.ReceivedAt)
        .Select(a => (DateTime?)a.ReceivedAt)
        .FirstOrDefaultAsync();
    var states = await db.State.ToDictionaryAsync(s => s.Key, s => s.Value);
    var devices = await db.Devices
        .Select(d => new { d.SerialNumber, d.LastSeenAt, online = d.LastSeenAt != null && d.LastSeenAt > DateTime.UtcNow.AddMinutes(-5) })
        .ToListAsync();
    return Results.Json(new
    {
        mode = "offline-first",
        pendingSync = pending,
        permanentFailures = failed,
        totalCollected = total,
        cachedEmployees = employees,
        oldestPendingRecord = oldestPending,
        centralOnline = states.GetValueOrDefault("centralOnline") == "true",
        lastSuccessfulSync = states.GetValueOrDefault("lastSuccessfulSync"),
        devices
    });
});

app.MapIclock();

app.Run();
