using System.Text;
using Hris.Api.Data;
using Hris.Api.Services;
using Hris.Api.Services.Biometric;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<HrisDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<ApprovalService>();
builder.Services.AddScoped<AttendanceSummaryService>();
builder.Services.AddScoped<PayrollDeductionService>();
builder.Services.AddScoped<PayrollService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<ISenseFaceDeviceAdapter, SenseFaceDeviceAdapter>();
builder.Services.AddScoped<BiometricEnrollmentService>();
builder.Services.AddScoped<SyncBatchMaintenanceService>();
builder.Services.AddHostedService<PayrollBackgroundService>();
builder.Services.AddHostedService<AttendanceMaintenanceHostedService>();
builder.Services.AddHostedService<SyncBatchMaintenanceHostedService>();

builder.Services.Configure<HostOptions>(o =>
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

var jwtKey = builder.Configuration["Jwt:Key"] ?? "CHANGE_ME_dev_signing_key_at_least_32_chars!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "hris",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "hris-clients",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddOpenApi();

var app = builder.Build();

// Create database and seed initial data.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HrisDbContext>();
    try
    {
        await DbSeeder.SeedAsync(db);
        await DbSchemaBootstrap.ApplyPerformanceSchemaAsync(db, app.Logger);
        await DbSchemaBootstrap.ApplyBiometricSchemaAsync(db, app.Logger);
        await DbSchemaBootstrap.ApplyLeavePolicyAsync(db, app.Logger);
        await DbSchemaBootstrap.ApplyExecutiveExemptionAsync(db, app.Logger);
        await DbSchemaBootstrap.ApplyApprovalSchemaAsync(db, app.Logger);
        await DbSchemaBootstrap.ApplySystemSettingsSchemaAsync(db, app.Logger);
        await DbSchemaBootstrap.ApplyEmployeeStatutorySchemaAsync(db, app.Logger);
        await DbSchemaBootstrap.ApplyPayrollDeductionSchemaAsync(db, app.Logger);
        var deductionSvc = scope.ServiceProvider.GetRequiredService<PayrollDeductionService>();
        await deductionSvc.EnsureDeductionTypesSeededAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Database seeding failed. Check the connection string in appsettings.json.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// serve uploaded files (employee photos / documents)
var uploads = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(uploads);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploads),
    RequestPath = "/uploads"
});

app.Run();
