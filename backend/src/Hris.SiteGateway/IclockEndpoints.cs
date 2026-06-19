using Microsoft.EntityFrameworkCore;

namespace Hris.SiteGateway;

/// <summary>
/// Implements the ZKTeco PUSH ("iclock") protocol that SenseFace 2A devices use.
/// Point the device's ADMS / Cloud Server setting at this gateway
/// (Server Address = gateway IP, Port = 8090) and it will push attendance
/// records here in real time, with no polling required.
/// </summary>
public static class IclockEndpoints
{
    public static void MapIclock(this WebApplication app)
    {
        // Handshake: device announces itself and asks for configuration.
        app.MapGet("/iclock/cdata", async (HttpContext ctx, LocalDbContext db, ILoggerFactory lf) =>
        {
            var log = lf.CreateLogger("iclock");
            var sn = ctx.Request.Query["SN"].ToString();
            if (string.IsNullOrEmpty(sn)) return Results.Text("ERROR: missing SN");

            await TouchDeviceAsync(db, sn, ctx.Request.Query["pushver"].ToString());
            log.LogInformation("Device {Sn} handshake from {Ip}", sn, ctx.Connection.RemoteIpAddress);

            var response =
                $"GET OPTION FROM: {sn}\r\n" +
                "ATTLOGStamp=None\r\n" +
                "OPERLOGStamp=None\r\n" +
                "ATTPHOTOStamp=None\r\n" +
                "ErrorDelay=30\r\n" +
                "Delay=10\r\n" +
                "TransTimes=00:00;14:05\r\n" +
                "TransInterval=1\r\n" +
                "TransFlag=TransData AttLog OpLog AttPhoto EnrollUser ChgUser EnrollFP ChgFP FPImag FACE UserPic\r\n" +
                "TimeZone=8\r\n" +
                "Realtime=1\r\n" +
                "Encrypt=None";
            return Results.Text(response);
        });

        // Data upload: device pushes ATTLOG (attendance) and OPERLOG (operations) tables.
        app.MapPost("/iclock/cdata", async (HttpContext ctx, LocalDbContext db, ILoggerFactory lf) =>
        {
            var log = lf.CreateLogger("iclock");
            var sn = ctx.Request.Query["SN"].ToString();
            var table = ctx.Request.Query["table"].ToString();
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();

            await TouchDeviceAsync(db, sn, null);

            if (table.Equals("ATTLOG", StringComparison.OrdinalIgnoreCase))
            {
                var processed = 0;
                foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    // Format: PIN <tab> yyyy-MM-dd HH:mm:ss <tab> status <tab> verify [<tab> workcode ...]
                    var parts = line.Split('\t');
                    if (parts.Length < 2) continue;
                    var pin = parts[0].Trim();
                    if (!DateTime.TryParse(parts[1].Trim(), out var punchTime)) continue;
                    var status = parts.Length > 2 && int.TryParse(parts[2], out var s) ? s : 0;
                    var verify = parts.Length > 3 ? parts[3].Trim() : null;

                    // Map device status codes to punch types: 0=in 1=out 2=break-out 3=break-in 4/5=ot in/out
                    var punchType = status switch { 0 => 1, 1 => 2, 2 => 4, 3 => 3, 4 => 1, 5 => 2, _ => 1 };
                    var verifyMode = verify switch { "1" => "fingerprint", "15" => "face", "2" => "password", "3" or "4" => "card", _ => verify };

                    var duplicate = await db.Attendance.AnyAsync(a =>
                        a.BiometricUserId == pin && a.PunchTime == punchTime && a.PunchType == punchType);
                    if (duplicate) continue;

                    db.Attendance.Add(new LocalAttendance
                    {
                        BiometricUserId = pin,
                        PunchTime = punchTime,
                        PunchType = punchType,
                        VerifyMode = verifyMode,
                        DeviceSerial = sn
                    });
                    processed++;
                }
                await db.SaveChangesAsync();
                log.LogInformation("Device {Sn}: stored {Count} attendance record(s)", sn, processed);
                return Results.Text($"OK: {processed}");
            }

            if (table.Equals("BIODATA", StringComparison.OrdinalIgnoreCase)
                || table.Equals("FACE", StringComparison.OrdinalIgnoreCase))
            {
                var processed = await StoreTemplateUploadAsync(db, sn, body, type: 1, fingerFromField: "Index");
                log.LogInformation("Device {Sn}: stored {Count} face template(s)", sn, processed);
                return Results.Text($"OK: {processed}");
            }

            if (table.Equals("FINGERTMP", StringComparison.OrdinalIgnoreCase)
                || table.Equals("FP", StringComparison.OrdinalIgnoreCase))
            {
                var processed = await StoreTemplateUploadAsync(db, sn, body, type: 2, fingerFromField: "FID");
                log.LogInformation("Device {Sn}: stored {Count} fingerprint template(s)", sn, processed);
                return Results.Text($"OK: {processed}");
            }

            // OPERLOG and other tables are acknowledged but only logged.
            log.LogDebug("Device {Sn}: received table {Table} ({Len} bytes)", sn, table, body.Length);
            return Results.Text("OK");
        });

        // Device polls for queued commands (user sync, reboot, etc.)
        app.MapGet("/iclock/getrequest", async (HttpContext ctx, LocalDbContext db) =>
        {
            var sn = ctx.Request.Query["SN"].ToString();
            var device = await TouchDeviceAsync(db, sn, null);
            if (device is not null && !string.IsNullOrEmpty(device.PendingCommands))
            {
                var cmd = device.PendingCommands;
                device.PendingCommands = null;
                await db.SaveChangesAsync();
                return Results.Text(cmd);
            }
            return Results.Text("OK");
        });

        // Device reports command execution results.
        app.MapPost("/iclock/devicecmd", async (HttpContext ctx, LocalDbContext db) =>
        {
            var sn = ctx.Request.Query["SN"].ToString();
            await TouchDeviceAsync(db, sn, null);
            return Results.Text("OK");
        });
    }

    private static async Task<LocalDevice?> TouchDeviceAsync(LocalDbContext db, string sn, string? firmware)
    {
        if (string.IsNullOrEmpty(sn)) return null;
        var device = await db.Devices.FirstOrDefaultAsync(d => d.SerialNumber == sn);
        if (device is null)
        {
            device = new LocalDevice { SerialNumber = sn };
            db.Devices.Add(device);
        }
        device.LastSeenAt = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(firmware)) device.FirmwareVersion = firmware;
        await db.SaveChangesAsync();
        return device;
    }

    /// <summary>Parses ZKTeco PUSH template lines (key=value pairs separated by tabs).</summary>
    private static async Task<int> StoreTemplateUploadAsync(
        LocalDbContext db, string deviceSerial, string body, int type, string fingerFromField)
    {
        var processed = 0;
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = ParseFields(line);
            if (!fields.TryGetValue("Pin", out var pin) && !fields.TryGetValue("PIN", out pin))
                pin = line.Split('\t').FirstOrDefault()?.Trim() ?? "";
            if (string.IsNullOrEmpty(pin)) continue;

            var template = fields.GetValueOrDefault("Tmp") ?? fields.GetValueOrDefault("TMP");
            if (string.IsNullOrEmpty(template)) continue;

            var finger = 0;
            if (fields.TryGetValue(fingerFromField, out var f) && int.TryParse(f, out var fi))
                finger = fi;

            var exists = await db.TemplateUploads.AnyAsync(t =>
                t.BiometricUserId == pin && t.Type == type && t.FingerIndex == finger && t.TemplateData == template && !t.Synced);
            if (exists) continue;

            db.TemplateUploads.Add(new LocalTemplateUpload
            {
                BiometricUserId = pin,
                Type = type,
                FingerIndex = finger,
                TemplateData = template,
                DeviceSerial = deviceSerial
            });
            processed++;
        }
        if (processed > 0) await db.SaveChangesAsync();
        return processed;
    }

    private static Dictionary<string, string> ParseFields(string line)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            map[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }
        return map;
    }
}
