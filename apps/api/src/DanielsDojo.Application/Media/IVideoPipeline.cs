using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Media;

namespace DanielsDojo.Application.Media;

/// <summary>An asset the video provider has been asked to create.</summary>
/// <param name="AssetId">Provider asset identifier.</param>
/// <param name="UploadId">
/// The provider's identifier for the ingest request, recorded so a webhook that arrives before
/// the asset identifier is known can still be matched to the right lesson.
/// </param>
/// <param name="Status">Where the asset is in the provider's own pipeline.</param>
public sealed record VideoIngestTicket(string? AssetId, string UploadId, LessonVideoStatus Status);

/// <summary>What the provider currently reports about an asset.</summary>
/// <param name="AssetId">Provider asset identifier.</param>
/// <param name="PlaybackId">Playback identifier, once one exists.</param>
/// <param name="Status">Mapped provider state.</param>
/// <param name="DurationSeconds">Measured duration, once known.</param>
/// <param name="AspectRatio">Reported aspect ratio, once known.</param>
/// <param name="FailureCode">Provider error code, when the asset failed.</param>
public sealed record VideoAssetState(
    string AssetId,
    string? PlaybackId,
    LessonVideoStatus Status,
    int? DurationSeconds,
    string? AspectRatio,
    string? FailureCode);

/// <summary>A verified inbound provider notification.</summary>
/// <param name="EventId">Provider event identifier, used to reject replays.</param>
/// <param name="EventType">Provider event type.</param>
/// <param name="AssetId">Asset the event concerns, when it names one.</param>
/// <param name="UploadId">Ingest request the event concerns, when it names one.</param>
/// <param name="OccurredAtUtc">
/// When the provider says it happened. Ordering is decided by this, not by arrival, because
/// notifications overtake one another.
/// </param>
/// <param name="State">Asset state carried by the event, when it carries one.</param>
public sealed record VideoProviderEvent(
    string EventId,
    string EventType,
    string? AssetId,
    string? UploadId,
    DateTimeOffset OccurredAtUtc,
    VideoAssetState? State);

/// <summary>A caption track registered with the provider.</summary>
/// <param name="TrackId">Provider track identifier.</param>
/// <param name="Status">Where the track is in the provider's pipeline.</param>
public sealed record VideoCaptionTicket(string TrackId, LessonVideoStatus Status);

/// <summary>
/// Video processing and signed playback.
/// </summary>
/// <remarks>
/// <para>
/// The provider is asked to pull the master from storage rather than being handed bytes, so
/// the original still makes exactly one journey — browser to storage — and the API never
/// buffers it.
/// </para>
/// <para>
/// There is no delete here either, for the same reason as storage: a processed asset can be
/// rebuilt from the verified master, but only while the master exists.
/// </para>
/// </remarks>
public interface IVideoPipeline
{
    /// <summary>Which adapter is serving.</summary>
    ProviderMode Mode { get; }

    /// <summary>Whether playback identifiers issued by this adapter require a signed token.</summary>
    bool UsesSignedPlayback { get; }

    /// <summary>Asks the provider to ingest a master it will fetch from the given location.</summary>
    Task<VideoIngestTicket> StartIngestAsync(
        Uri sourceReadUri,
        string correlationKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads current provider state for an asset, or null when the provider does not know it.
    /// This is what reconciliation uses to repair a lesson whose webhook never arrived.
    /// </summary>
    Task<VideoAssetState?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default);

    /// <summary>Registers a caption track against an existing asset.</summary>
    Task<VideoCaptionTicket> AddCaptionTrackAsync(
        string assetId,
        Uri captionReadUri,
        string languageCode,
        string displayName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies an inbound notification and parses it, or returns null when the signature,
    /// timestamp, or payload does not hold up.
    /// </summary>
    /// <remarks>
    /// Returning null rather than throwing keeps an unsigned or stale delivery from becoming an
    /// error-log entry an attacker can generate at will.
    /// </remarks>
    VideoProviderEvent? VerifyEvent(string payload, string? signatureHeader, DateTimeOffset now);

    /// <summary>
    /// Mints a viewing token for a playback identifier. The token is short-lived and is issued
    /// per viewer request, so a shared link stops working quickly.
    /// </summary>
    string CreatePlaybackToken(string playbackId, DateTimeOffset expiresAtUtc);
}
