namespace DanielsDojo.Application.System;

/// <summary>
/// Immutable, strongly-typed system-status contract returned by the Phase 1
/// vertical slice. It deliberately exposes only safe, non-sensitive fields.
/// </summary>
/// <param name="Status">Coarse health indicator, e.g. <c>"ok"</c>.</param>
/// <param name="Service">Stable human-readable service name.</param>
/// <param name="Environment">Host environment name (e.g. Development, Production).</param>
/// <param name="UtcTimestamp">Server time in UTC at the moment the response was built.</param>
public sealed record SystemStatus(
    string Status,
    string Service,
    string Environment,
    DateTime UtcTimestamp);
