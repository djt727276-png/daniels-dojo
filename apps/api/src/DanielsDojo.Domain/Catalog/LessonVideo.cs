using DanielsDojo.Domain.Media;

namespace DanielsDojo.Domain.Catalog;

/// <summary>
/// Video provider metadata for one lesson.
/// </summary>
/// <remarks>
/// <para>
/// Metadata only — no video bytes, signed playback tokens, or provider secrets are stored. The
/// exact source master lives in blob storage and is described by <see cref="MediaSource"/>; the
/// processing provider supplies adaptive delivery and is explicitly not a backup of the
/// original.
/// </para>
/// <para>
/// The last-known-good columns exist so a failed replacement degrades to "the previous video
/// still plays" rather than "the lesson is broken". They are cleared only by an explicit
/// operator archive, never by a provider event.
/// </para>
/// </remarks>
public sealed class LessonVideo
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning lesson. Unique: at most one video record per lesson.</summary>
    public Guid LessonId { get; set; }

    /// <summary>The exact source master currently associated with this lesson.</summary>
    public Guid? CurrentSourceId { get; set; }

    /// <summary>A verified replacement waiting to be promoted, if one is in flight.</summary>
    public Guid? IncomingSourceId { get; set; }

    /// <summary>Provider upload handle, when ingestion was started by direct upload.</summary>
    public string? MuxUploadId { get; set; }

    /// <summary>Provider asset identifier. Unique when present.</summary>
    public string? MuxAssetId { get; set; }

    /// <summary>Provider playback identifier. Unique when present.</summary>
    public string? MuxPlaybackId { get; set; }

    /// <summary>
    /// Whether the playback identifier requires a signed token. Protected course content is
    /// always signed; nothing in the catalog is served from a public playback identifier.
    /// </summary>
    public bool IsSignedPlayback { get; set; } = true;

    /// <summary>Asset that was serving before the current replacement began.</summary>
    public string? LastKnownGoodAssetId { get; set; }

    /// <summary>Playback identifier that was serving before the current replacement began.</summary>
    public string? LastKnownGoodPlaybackId { get; set; }

    /// <summary>Provider processing state.</summary>
    public LessonVideoStatus Status { get; set; } = LessonVideoStatus.Requested;

    /// <summary>Which provider wiring produced the current provider identifiers.</summary>
    public ProviderMode ProviderMode { get; set; }

    /// <summary>Measured duration reported by the provider.</summary>
    public int? DurationSeconds { get; set; }

    /// <summary>Aspect ratio reported by the provider, for example "16:9".</summary>
    public string? AspectRatio { get; set; }

    /// <summary>Short internal failure code when <see cref="Status"/> is Failed.</summary>
    public string? FailureCode { get; set; }

    /// <summary>
    /// When the newest applied provider event was raised, stored UTC. An event older than this
    /// is discarded, which is what stops a late duplicate from undoing a newer replacement.
    /// </summary>
    public DateTimeOffset? LastProviderEventAtUtc { get; set; }

    /// <summary>When an authorised Admin last played the protected stream, stored UTC.</summary>
    public DateTimeOffset? AdminPlaybackVerifiedAtUtc { get; set; }

    /// <summary>When an authorised Student last played the published stream, stored UTC.</summary>
    public DateTimeOffset? StudentPlaybackVerifiedAtUtc { get; set; }

    /// <summary>When a person confirmed the beginning, middle, and end are watchable.</summary>
    public DateTimeOffset? HumanSpotCheckAtUtc { get; set; }

    /// <summary>Who recorded the human spot check.</summary>
    public Guid? HumanSpotCheckByUserId { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token; provider events update this row.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The owning lesson.</summary>
    public Lesson? Lesson { get; set; }

    /// <summary>The exact source master currently associated with this lesson.</summary>
    public MediaSource? CurrentSource { get; set; }

    /// <summary>Caption tracks attached to this video.</summary>
    public ICollection<MediaCaptionTrack> CaptionTracks { get; } = new List<MediaCaptionTrack>();

    /// <summary>
    /// The playback identifier a viewer should be served: the current asset when ready, and
    /// the previous known-good one while a replacement is still in flight.
    /// </summary>
    public string? ServablePlaybackId => Status switch
    {
        LessonVideoStatus.Ready => MuxPlaybackId,
        LessonVideoStatus.Replacing => LastKnownGoodPlaybackId ?? MuxPlaybackId,
        _ => null,
    };
}
