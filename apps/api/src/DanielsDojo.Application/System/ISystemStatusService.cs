namespace DanielsDojo.Application.System;

/// <summary>
/// Builds the current <see cref="SystemStatus"/> for the system-status slice.
/// </summary>
public interface ISystemStatusService
{
    /// <summary>Creates a status snapshot using the injected time and environment.</summary>
    SystemStatus GetStatus();
}
