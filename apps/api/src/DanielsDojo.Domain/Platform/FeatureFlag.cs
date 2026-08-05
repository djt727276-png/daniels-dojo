namespace DanielsDojo.Domain.Platform;

/// <summary>
/// An operator-controlled switch.
/// </summary>
/// <remarks>
/// Flags are kill switches, not configuration: a missing row means the feature runs with its
/// built-in default, so deleting a flag can never brick anything and the table starts empty.
/// Every consumer reads its flag fail-safe at the point of use.
/// </remarks>
public sealed class FeatureFlag
{
    /// <summary>Stable key the consuming code reads, for example "checkout".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Whether the feature is on.</summary>
    public bool Enabled { get; set; }

    /// <summary>What flipping this actually does, for the operator reading the list.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last toggle instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
