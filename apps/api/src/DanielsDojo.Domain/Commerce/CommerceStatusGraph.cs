namespace DanielsDojo.Domain.Commerce;

/// <summary>
/// Which commerce status changes are legal.
/// </summary>
/// <remarks>
/// Retired is terminal. An order, subscription, or entitlement references the exact offer and
/// price it was sold under, so bringing a withdrawn one back to life would silently change what
/// those records mean. Selling again means publishing a new row.
/// </remarks>
public static class CommerceStatusGraph
{
    /// <summary>Whether <paramref name="target"/> may be reached from <paramref name="current"/>.</summary>
    public static bool CanTransition(CommerceStatus current, CommerceStatus target) =>
        (current, target) switch
        {
            (CommerceStatus.Draft, CommerceStatus.Active) => true,
            (CommerceStatus.Draft, CommerceStatus.Retired) => true,
            (CommerceStatus.Active, CommerceStatus.Retired) => true,
            _ => false,
        };

    /// <summary>The statuses reachable from <paramref name="current"/>.</summary>
    public static IReadOnlyList<CommerceStatus> AllowedTargets(CommerceStatus current) =>
        Enum.GetValues<CommerceStatus>()
            .Where(target => CanTransition(current, target))
            .ToArray();

    /// <summary>Whether a record in this status may still be edited.</summary>
    public static bool IsEditable(CommerceStatus status) => status == CommerceStatus.Draft;
}
