using Microsoft.EntityFrameworkCore;

namespace Hris.SiteGateway;

/// <summary>Upgrades existing local gateway databases (EnsureCreated does not alter tables).</summary>
public static class LocalGatewayBootstrap
{
    public static async Task ApplyAsync(LocalDbContext db, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            var provider = db.Database.ProviderName ?? "";
            if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
                await ApplySqliteAsync(db, ct);
            else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
                await ApplySqlServerAsync(db, ct);

            logger.LogInformation("Local gateway schema bootstrap applied.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Local gateway schema bootstrap skipped or partially applied.");
        }
    }

    private static async Task ApplySqliteAsync(LocalDbContext db, CancellationToken ct)
    {
        await TryAddColumn(db, "Attendance", "LastSyncError", "TEXT NULL", ct);
        await TryAddColumn(db, "Attendance", "NextRetryAt", "TEXT NULL", ct);
        await TryAddColumn(db, "Attendance", "PermanentFailure", "INTEGER NOT NULL DEFAULT 0", ct);
        await TryAddColumn(db, "TemplateUploads", "SyncAttempts", "INTEGER NOT NULL DEFAULT 0", ct);
        await TryAddColumn(db, "TemplateUploads", "LastSyncError", "TEXT NULL", ct);
        await TryAddColumn(db, "TemplateUploads", "NextRetryAt", "TEXT NULL", ct);
    }

    private static async Task ApplySqlServerAsync(LocalDbContext db, CancellationToken ct)
    {
        await Exec(db, "IF COL_LENGTH('Attendance','LastSyncError') IS NULL ALTER TABLE Attendance ADD LastSyncError nvarchar(max) NULL;", ct);
        await Exec(db, "IF COL_LENGTH('Attendance','NextRetryAt') IS NULL ALTER TABLE Attendance ADD NextRetryAt datetime2 NULL;", ct);
        await Exec(db, "IF COL_LENGTH('Attendance','PermanentFailure') IS NULL ALTER TABLE Attendance ADD PermanentFailure bit NOT NULL CONSTRAINT DF_Attendance_PermFail DEFAULT 0;", ct);
        await Exec(db, "IF COL_LENGTH('TemplateUploads','SyncAttempts') IS NULL ALTER TABLE TemplateUploads ADD SyncAttempts int NOT NULL CONSTRAINT DF_TemplateUploads_Attempts DEFAULT 0;", ct);
        await Exec(db, "IF COL_LENGTH('TemplateUploads','LastSyncError') IS NULL ALTER TABLE TemplateUploads ADD LastSyncError nvarchar(max) NULL;", ct);
        await Exec(db, "IF COL_LENGTH('TemplateUploads','NextRetryAt') IS NULL ALTER TABLE TemplateUploads ADD NextRetryAt datetime2 NULL;", ct);
    }

    private static async Task TryAddColumn(LocalDbContext db, string table, string column, string type, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
            var exists = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
            if (!exists)
                await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD COLUMN {column} {type}", ct);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    private static Task Exec(LocalDbContext db, string sql, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(sql, ct);
}
