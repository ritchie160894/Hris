using Hris.Domain;
using Hris.Domain.Entities;

namespace Hris.Api.Services.Biometric;

/// <summary>
/// Abstraction for SenseFace 2A / ZKTeco device operations.
/// Default production path uses iclock PUSH via the site gateway (no SDK required).
/// Implement this interface when you have the vendor SDK for direct TCP enrollment (port 4370).
/// </summary>
public interface ISenseFaceDeviceAdapter
{
    /// <summary>When true, enrollment is triggered directly on the device IP (SDK). When false, commands go through the site gateway iclock protocol.</summary>
    bool SupportsDirectEnrollment { get; }

    /// <summary>Build the iclock PUSH command that puts the device into enrollment mode for the given user.</summary>
    string BuildEnrollmentCommand(BiometricEnrollment enrollment, Employee employee, BiometricDevice device);

    /// <summary>Optional: call vendor SDK to start enrollment on device IP. Not required when using gateway PUSH.</summary>
    Task<SenseFaceEnrollmentResult> StartDirectEnrollmentAsync(
        BiometricEnrollment enrollment, Employee employee, BiometricDevice device, CancellationToken ct = default);

    /// <summary>Optional: simulated completion for development without hardware.</summary>
    Task<SenseFaceEnrollmentResult> SimulateCaptureAsync(
        BiometricEnrollment enrollment, Employee employee, CancellationToken ct = default);
}

public record SenseFaceEnrollmentResult(bool Success, string? TemplateDataBase64, string? ErrorMessage);
