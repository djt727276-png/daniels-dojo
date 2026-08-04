using DanielsDojo.Domain.Catalog;

namespace DanielsDojo.Domain.Media;

/// <summary>
/// A caption or subtitle track attached to a video lesson.
/// </summary>
/// <remarks>
/// Captions are stored as their own source object and associated with the processing provider
/// separately from the video, so a caption can be corrected without re-ingesting the master.
/// </remarks>
public sealed class MediaCaptionTrack
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Video the track belongs to.</summary>
    public Guid LessonVideoId { get; set; }

    /// <summary>Source object holding the caption file.</summary>
    public Guid MediaSourceId { get; set; }

    /// <summary>BCP-47 language tag, for example "en".</summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>Name shown in the player's track menu.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Whether the player should select this track by default.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Provider track identifier. Unique when present.</summary>
    public string? ProviderTrackId { get; set; }

    /// <summary>Lifecycle state, mirroring the video's own vocabulary.</summary>
    public LessonVideoStatus Status { get; set; } = LessonVideoStatus.Requested;

    /// <summary>Stable internal failure category. Never a raw provider body.</summary>
    public string? FailureCode { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The owning video.</summary>
    public LessonVideo? LessonVideo { get; set; }

    /// <summary>The stored caption file.</summary>
    public MediaSource? MediaSource { get; set; }
}
