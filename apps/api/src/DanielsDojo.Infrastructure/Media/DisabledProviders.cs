using DanielsDojo.Application.Media;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Media;

namespace DanielsDojo.Infrastructure.Media;

/// <summary>
/// The adapter used when storage is switched off.
/// </summary>
/// <remarks>
/// It refuses rather than pretending. A no-op that returned a plausible authorisation would let
/// an administrator believe a master had been stored when nothing had, which is the one failure
/// this workstream cannot afford — the local original gets deleted on the strength of exactly
/// that belief.
/// </remarks>
internal sealed class DisabledMediaStorage : IMediaStorage
{
    public ProviderMode Mode => ProviderMode.Disabled;

    public Task<MediaUploadAuthorization> AuthorizeUploadAsync(
        string containerName,
        string blobName,
        string contentType,
        long declaredSizeBytes,
        CancellationToken cancellationToken = default) => throw Refuse();

    public Task<MediaObjectProperties?> GetPropertiesAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default) => throw Refuse();

    public Task<MediaRestoreProbe?> ProbeRestoreAsync(
        string containerName,
        string blobName,
        long maxBytes,
        CancellationToken cancellationToken = default) => throw Refuse();

    public Task<Uri> AuthorizeIngestReadAsync(
        string containerName,
        string blobName,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default) => throw Refuse();

    private static InvalidOperationException Refuse() =>
        new($"Media storage is disabled. Set {MediaStorageOptions.SectionName}:Mode to "
            + "Deterministic or Real before using the media pipeline.");
}

/// <summary>The adapter used when video processing is switched off.</summary>
/// <remarks>
/// Storage can be on while this is off, which is a legitimate state: the master is captured and
/// verified, and the lesson simply is not playable yet. That is why the service checks the mode
/// and records <see cref="LessonVideoStatus.AzureStored"/> rather than calling into here.
/// </remarks>
internal sealed class DisabledVideoPipeline : IVideoPipeline
{
    public ProviderMode Mode => ProviderMode.Disabled;

    public bool UsesSignedPlayback => false;

    public Task<VideoIngestTicket> StartIngestAsync(
        Uri sourceReadUri,
        string correlationKey,
        CancellationToken cancellationToken = default) => throw Refuse();

    public Task<VideoAssetState?> GetAssetAsync(
        string assetId,
        CancellationToken cancellationToken = default) => throw Refuse();

    public Task<VideoCaptionTicket> AddCaptionTrackAsync(
        string assetId,
        Uri captionReadUri,
        string languageCode,
        string displayName,
        CancellationToken cancellationToken = default) => throw Refuse();

    /// <summary>
    /// Always refuses. An unverifiable notification must be rejected, not ignored quietly, so a
    /// deployment with the provider switched off cannot be driven by anonymous requests.
    /// </summary>
    public VideoProviderEvent? VerifyEvent(string payload, string? signatureHeader, DateTimeOffset now) =>
        null;

    public string CreatePlaybackToken(string playbackId, DateTimeOffset expiresAtUtc) => throw Refuse();

    private static InvalidOperationException Refuse() =>
        new($"Video processing is disabled. Set {VideoProviderOptions.SectionName}:Mode to "
            + "Deterministic or Real before using the media pipeline.");
}
