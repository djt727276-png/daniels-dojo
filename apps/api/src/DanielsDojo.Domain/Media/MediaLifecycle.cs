using DanielsDojo.Domain.Catalog;

namespace DanielsDojo.Domain.Media;

/// <summary>
/// The legal moves through media processing, stated once.
/// </summary>
/// <remarks>
/// <para>
/// Provider webhooks arrive late, out of order, and more than once. A transition table is what
/// lets an older event be recognised and dropped instead of dragging a lesson backwards from
/// <see cref="LessonVideoStatus.Ready"/> into <see cref="LessonVideoStatus.Processing"/> because
/// a duplicate turned up after the real one.
/// </para>
/// <para>
/// <see cref="LessonVideoStatus.Replacing"/> is deliberately reachable only from
/// <see cref="LessonVideoStatus.Ready"/>: you can only replace something that currently works,
/// and while replacing, the previous asset stays the one being served.
/// </para>
/// </remarks>
public static class MediaLifecycle
{
    /// <summary>States in which the lesson has a playable asset a member could be served.</summary>
    public static bool IsPlayable(LessonVideoStatus status) =>
        status is LessonVideoStatus.Ready or LessonVideoStatus.Replacing;

    /// <summary>States from which no further provider work is expected.</summary>
    public static bool IsTerminal(LessonVideoStatus status) =>
        status is LessonVideoStatus.Archived;

    /// <summary>Whether <paramref name="target"/> may be reached from <paramref name="current"/>.</summary>
    public static bool CanTransition(LessonVideoStatus current, LessonVideoStatus target) =>
        (current, target) switch
        {
            // A fresh attempt, or a retry after a failure.
            (LessonVideoStatus.Requested, LessonVideoStatus.Uploading) => true,
            (LessonVideoStatus.Failed, LessonVideoStatus.Requested) => true,
            (LessonVideoStatus.Failed, LessonVideoStatus.Uploading) => true,

            // The bytes landed and were verified against the trusted blob properties.
            (LessonVideoStatus.Uploading, LessonVideoStatus.AzureStored) => true,

            // Handing the exact stored object to the processing provider.
            (LessonVideoStatus.AzureStored, LessonVideoStatus.MuxIngesting) => true,
            (LessonVideoStatus.MuxIngesting, LessonVideoStatus.Processing) => true,
            (LessonVideoStatus.Processing, LessonVideoStatus.Ready) => true,

            // The provider can report readiness before we observe the intermediate step.
            (LessonVideoStatus.AzureStored, LessonVideoStatus.Ready) => true,
            (LessonVideoStatus.MuxIngesting, LessonVideoStatus.Ready) => true,

            // Replacing an asset that currently works, and the two ways that ends.
            (LessonVideoStatus.Ready, LessonVideoStatus.Replacing) => true,
            (LessonVideoStatus.Replacing, LessonVideoStatus.Ready) => true,
            (LessonVideoStatus.Replacing, LessonVideoStatus.Failed) => true,

            // Anything in flight can fail.
            (LessonVideoStatus.Requested, LessonVideoStatus.Failed) => true,
            (LessonVideoStatus.Uploading, LessonVideoStatus.Failed) => true,
            (LessonVideoStatus.AzureStored, LessonVideoStatus.Failed) => true,
            (LessonVideoStatus.MuxIngesting, LessonVideoStatus.Failed) => true,
            (LessonVideoStatus.Processing, LessonVideoStatus.Failed) => true,

            // Withdrawing media is an operator decision and is one-way.
            (_, LessonVideoStatus.Archived) when current != LessonVideoStatus.Archived => true,

            _ => false,
        };

    /// <summary>
    /// Whether a provider event observed at <paramref name="eventAtUtc"/> is newer than what has
    /// already been applied, so a delayed duplicate cannot overwrite fresher truth.
    /// </summary>
    public static bool IsNewerThan(DateTimeOffset eventAtUtc, DateTimeOffset? appliedAtUtc) =>
        appliedAtUtc is null || eventAtUtc > appliedAtUtc;

    /// <summary>The statuses reachable from <paramref name="current"/>.</summary>
    public static IReadOnlyList<LessonVideoStatus> AllowedTargets(LessonVideoStatus current) =>
        Enum.GetValues<LessonVideoStatus>()
            .Where(target => CanTransition(current, target))
            .ToArray();
}
