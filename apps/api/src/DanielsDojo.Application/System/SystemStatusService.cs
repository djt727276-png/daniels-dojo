namespace DanielsDojo.Application.System;

/// <summary>
/// Default <see cref="ISystemStatusService"/>. Time is obtained from the injectable
/// <see cref="TimeProvider"/> abstraction (never the ambient wall clock), and the
/// environment name comes from <see cref="IApplicationEnvironment"/>.
/// </summary>
public sealed class SystemStatusService(TimeProvider timeProvider, IApplicationEnvironment environment)
    : ISystemStatusService
{
    /// <summary>Stable service name exposed by the status contract.</summary>
    public const string ServiceName = "Daniel's Dojo API";

    /// <summary>Coarse healthy status token.</summary>
    public const string OkStatus = "ok";

    /// <inheritdoc />
    public SystemStatus GetStatus() => new(
        Status: OkStatus,
        Service: ServiceName,
        Environment: environment.EnvironmentName,
        UtcTimestamp: timeProvider.GetUtcNow().UtcDateTime);
}
