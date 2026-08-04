namespace DanielsDojo.Domain.Catalog;

/// <summary>
/// The single definition of which publication status changes are legal.
/// </summary>
/// <remarks>
/// Stated once, in the domain, so courses, sections, and lessons cannot drift apart. Archived
/// is deliberately a one-way door back to Draft: restoring a record straight to Published would
/// republish content nobody has re-read since it was withdrawn.
/// </remarks>
public static class PublicationStatusGraph
{
    /// <summary>Whether <paramref name="target"/> may be reached from <paramref name="current"/>.</summary>
    public static bool CanTransition(PublicationStatus current, PublicationStatus target) =>
        (current, target) switch
        {
            (PublicationStatus.Draft, PublicationStatus.Published) => true,
            (PublicationStatus.Draft, PublicationStatus.Archived) => true,
            (PublicationStatus.Published, PublicationStatus.Draft) => true,
            (PublicationStatus.Published, PublicationStatus.Archived) => true,
            (PublicationStatus.Archived, PublicationStatus.Draft) => true,
            _ => false,
        };

    /// <summary>The statuses reachable from <paramref name="current"/>.</summary>
    public static IReadOnlyList<PublicationStatus> AllowedTargets(PublicationStatus current) =>
        Enum.GetValues<PublicationStatus>()
            .Where(target => CanTransition(current, target))
            .ToArray();
}
