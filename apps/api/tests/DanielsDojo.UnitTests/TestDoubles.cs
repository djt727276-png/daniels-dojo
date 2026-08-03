using DanielsDojo.Application.System;

namespace DanielsDojo.UnitTests;

/// <summary>
/// Deterministic <see cref="TimeProvider"/> that always returns a fixed instant, so
/// UTC/timestamp behaviour is verified without sleeping or reading the wall clock.
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset fixedUtcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => fixedUtcNow;
}

/// <summary>Simple <see cref="IApplicationEnvironment"/> stub for unit tests.</summary>
internal sealed class StubApplicationEnvironment(string environmentName) : IApplicationEnvironment
{
    public string EnvironmentName { get; } = environmentName;
}
