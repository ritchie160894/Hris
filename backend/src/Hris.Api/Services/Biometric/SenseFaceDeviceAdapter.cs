using Hris.Domain;
using Hris.Domain.Entities;

namespace Hris.Api.Services.Biometric;

/// <summary>
/// SenseFace 2A adapter — iclock PUSH commands now; plug vendor SDK into <see cref="StartDirectEnrollmentAsync"/> later.
/// </summary>
public class SenseFaceDeviceAdapter(IConfiguration config, ILogger<SenseFaceDeviceAdapter> logger) : ISenseFaceDeviceAdapter
{
    public bool SupportsDirectEnrollment =>
        string.Equals(config["Biometric:Provider"], "SenseFaceSdk", StringComparison.OrdinalIgnoreCase);

    public string BuildEnrollmentCommand(BiometricEnrollment enrollment, Employee employee, BiometricDevice device)
    {
        var pin = employee.BiometricUserId ?? employee.EmployeeCode;
        var cmdId = enrollment.Id;

        // Ensure user exists on device before enrollment
        var ensureUser = $"C:{cmdId}:DATA UPDATE USERINFO PIN={pin}\tName={employee.FullName}\tPri=0\tPasswd=\tCard=\tGrp=1\tTZ=1\tVerify=0";

        if (enrollment.Type == BiometricTemplateType.Face)
        {
            // ZKTeco PUSH: start face enrollment — employee scans at the device
            var enroll = $"C:{cmdId + 1}:ENROLL BIO PIN={pin}";
            return $"{ensureUser}\r\n{enroll}";
        }

        var finger = enrollment.FingerIndex;
        var enrollFp = $"C:{cmdId + 1}:ENROLL FP PIN={pin}\tFID={finger}";
        return $"{ensureUser}\r\n{enrollFp}";
    }

    public Task<SenseFaceEnrollmentResult> StartDirectEnrollmentAsync(
        BiometricEnrollment enrollment, Employee employee, BiometricDevice device, CancellationToken ct = default)
    {
        // -------------------------------------------------------------------------
        // TODO: When you receive the SenseFace 2A SDK, implement direct TCP calls here.
        // Typical steps (ZKTeco zkemkeeper / SenseFace SDK):
        //   1. Connect(device.IpAddress, device.Port)
        //   2. SSR_SetUserInfo(pin, name, ...)
        //   3. StartEnrollFace(pin) or StartEnrollFingerprint(pin, fingerIndex)
        //   4. Wait for capture event / poll template
        //   5. GetUserFaceTemplate / GetUserTmpExStr → base64 template
        //   6. Disconnect
        // Set appsettings Biometric:Provider = "SenseFaceSdk" to enable this path.
        // -------------------------------------------------------------------------
        logger.LogWarning(
            "SenseFace SDK direct enrollment not configured. Enrollment {Id} will use site gateway iclock PUSH instead.",
            enrollment.Id);

        return Task.FromResult(new SenseFaceEnrollmentResult(
            false,
            null,
            "SenseFace SDK not installed. Using gateway PUSH protocol — employee must enroll at the physical device."));
    }

    public async Task<SenseFaceEnrollmentResult> SimulateCaptureAsync(
        BiometricEnrollment enrollment, Employee employee, CancellationToken ct = default)
    {
        var delay = config.GetValue("Biometric:SimulatedDelaySeconds", 3);
        await Task.Delay(TimeSpan.FromSeconds(delay), ct);

        var pin = employee.BiometricUserId ?? employee.EmployeeCode;
        var payload = enrollment.Type == BiometricTemplateType.Face
            ? $"SIM-FACE-{pin}-v{DateTime.UtcNow.Ticks}"
            : $"SIM-FP-{pin}-F{enrollment.FingerIndex}-v{DateTime.UtcNow.Ticks}";

        var template = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload));
        logger.LogInformation("Simulated {Type} capture for employee {Pin} (enrollment {Id}).",
            enrollment.Type, pin, enrollment.Id);

        return new SenseFaceEnrollmentResult(true, template, null);
    }
}
