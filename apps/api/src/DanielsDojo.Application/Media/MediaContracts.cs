namespace DanielsDojo.Application.Media;

/// <summary>What an administrator says they are about to upload.</summary>
/// <param name="FileName">Original file name, kept for display only.</param>
/// <param name="ContentType">Declared content type.</param>
/// <param name="SizeBytes">Declared size, checked against the configured ceiling.</param>
public sealed record MediaUploadRequest(string FileName, string ContentType, long SizeBytes);

/// <summary>An authorisation the browser uses to upload one object.</summary>
/// <param name="SessionId">Identifier the client returns when the upload finishes.</param>
/// <param name="UploadUri">Where to write. Short-lived, write-only, one object.</param>
/// <param name="HttpMethod">Method the upload must use.</param>
/// <param name="RequiredHeaders">Headers the write must carry.</param>
/// <param name="ExpiresAtUtc">When the authorisation stops working.</param>
/// <param name="ProviderMode">Which adapter issued it, so the UI can say so plainly.</param>
public sealed record MediaUploadTicket(
    Guid SessionId,
    Uri UploadUri,
    string HttpMethod,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAtUtc,
    string ProviderMode);

/// <summary>Everything recorded about one stored master.</summary>
/// <param name="Id">Source identifier.</param>
/// <param name="ContainerName">Container holding it.</param>
/// <param name="BlobName">Object name.</param>
/// <param name="ContentLength">Bytes the service holds.</param>
/// <param name="ContentType">Recorded content type.</param>
/// <param name="ChecksumSha256">Hash computed from bytes read back, when verified.</param>
/// <param name="State">Whether this is the current master, superseded, or still pending.</param>
/// <param name="PropertiesVerifiedAtUtc">When the service's own record was confirmed.</param>
/// <param name="RestoreVerifiedAtUtc">When bytes were successfully read back.</param>
/// <param name="RestoreVerifiedLength">The length the read-back reported.</param>
public sealed record MediaSourceEvidence(
    Guid Id,
    string ContainerName,
    string BlobName,
    long ContentLength,
    string ContentType,
    string? ChecksumSha256,
    string State,
    DateTimeOffset? PropertiesVerifiedAtUtc,
    DateTimeOffset? RestoreVerifiedAtUtc,
    long? RestoreVerifiedLength);

/// <summary>
/// The evidence trail behind one lesson's video, and the single answer it exists to produce.
/// </summary>
/// <param name="CloudPropertiesVerified">The service confirmed it holds the object.</param>
/// <param name="RestoreVerified">Bytes came back out, and the length matched.</param>
/// <param name="ProviderReady">The processed asset is playable.</param>
/// <param name="AdminPlaybackVerifiedAtUtc">An administrator successfully played it back.</param>
/// <param name="StudentPlaybackVerifiedAtUtc">A student-path token was successfully issued.</param>
/// <param name="HumanSpotCheckAtUtc">A human confirmed the footage is the right footage.</param>
/// <param name="SafeToDeleteLocalOriginal">
/// True only when every step above has passed. This is the one flag a person should read before
/// removing an original from their own machine, and nothing in the application acts on it — the
/// deletion is always a human decision taken outside this system.
/// </param>
public sealed record MediaVerificationEvidence(
    bool CloudPropertiesVerified,
    bool RestoreVerified,
    bool ProviderReady,
    DateTimeOffset? AdminPlaybackVerifiedAtUtc,
    DateTimeOffset? StudentPlaybackVerifiedAtUtc,
    DateTimeOffset? HumanSpotCheckAtUtc,
    bool SafeToDeleteLocalOriginal);

/// <summary>One caption track on a lesson video.</summary>
/// <param name="Id">Track identifier.</param>
/// <param name="LanguageCode">BCP-47 language code.</param>
/// <param name="DisplayName">Label shown in the player.</param>
/// <param name="IsDefault">Whether the player selects it by default.</param>
/// <param name="Status">Where the track is in the provider pipeline.</param>
public sealed record CaptionTrackView(
    Guid Id,
    string LanguageCode,
    string DisplayName,
    bool IsDefault,
    string Status);

/// <summary>The Admin view of one lesson's video.</summary>
/// <param name="LessonId">Lesson the video belongs to.</param>
/// <param name="VideoId">Video identifier, when one exists.</param>
/// <param name="Status">Lifecycle state.</param>
/// <param name="ProviderMode">Which adapter produced the current state.</param>
/// <param name="IsPlayable">Whether a student could play it right now.</param>
/// <param name="DurationSeconds">Measured duration, once known.</param>
/// <param name="AspectRatio">Reported aspect ratio, once known.</param>
/// <param name="FailureCode">Why it failed, when it did.</param>
/// <param name="CurrentSource">The master currently serving.</param>
/// <param name="IncomingSource">A replacement master still being processed.</param>
/// <param name="Captions">Caption tracks.</param>
/// <param name="Verification">The evidence trail.</param>
/// <param name="RowVersion">Concurrency token.</param>
public sealed record LessonVideoView(
    Guid LessonId,
    Guid? VideoId,
    string Status,
    string ProviderMode,
    bool IsPlayable,
    int? DurationSeconds,
    string? AspectRatio,
    string? FailureCode,
    MediaSourceEvidence? CurrentSource,
    MediaSourceEvidence? IncomingSource,
    IReadOnlyList<CaptionTrackView> Captions,
    MediaVerificationEvidence Verification,
    string? RowVersion);

/// <summary>A viewer's authorisation to play one lesson.</summary>
/// <param name="LessonId">Lesson being played.</param>
/// <param name="PlaybackId">Provider playback identifier.</param>
/// <param name="Token">
/// Short-lived viewing token, when the provider requires one. Never logged, never stored, and
/// issued per request rather than embedded in a cached response.
/// </param>
/// <param name="ExpiresAtUtc">When the authorisation stops working.</param>
/// <param name="DurationSeconds">Measured duration, for the player's timeline.</param>
/// <param name="AspectRatio">Reported aspect ratio, so the player reserves the right box.</param>
/// <param name="Captions">Caption tracks available to this viewer.</param>
/// <param name="AccessReason">Which grant permitted this, for the UI to explain.</param>
public sealed record LessonPlaybackGrant(
    Guid LessonId,
    string PlaybackId,
    string? Token,
    DateTimeOffset ExpiresAtUtc,
    int? DurationSeconds,
    string? AspectRatio,
    IReadOnlyList<CaptionTrackView> Captions,
    string AccessReason);

/// <summary>What one reconciliation pass changed.</summary>
/// <param name="Examined">Videos inspected.</param>
/// <param name="Repaired">Videos whose recorded state was corrected from the provider.</param>
/// <param name="StillPending">Videos the provider has not finished yet.</param>
/// <param name="Unreachable">Videos the provider no longer knows about.</param>
public sealed record MediaReconciliationReport(
    int Examined,
    int Repaired,
    int StillPending,
    int Unreachable);

/// <summary>Stable error codes for the media surface.</summary>
public static class MediaErrorCodes
{
    /// <summary>The configured provider mode does not support the request.</summary>
    public const string ProviderDisabled = "media.provider_disabled";

    /// <summary>The upload authorisation has expired or was already used.</summary>
    public const string SessionClosed = "media.session_closed";

    /// <summary>Storage holds nothing at the authorised location.</summary>
    public const string UploadMissing = "media.upload_missing";

    /// <summary>The stored object does not match what was declared.</summary>
    public const string UploadMismatch = "media.upload_mismatch";

    /// <summary>The declared size exceeds the configured ceiling.</summary>
    public const string UploadTooLarge = "media.upload_too_large";

    /// <summary>The declared content type is not an accepted one.</summary>
    public const string UnsupportedContentType = "media.unsupported_content_type";

    /// <summary>The lesson's media is not playable yet.</summary>
    public const string NotReady = "media.not_ready";

    /// <summary>The requested lifecycle transition is not permitted from the current state.</summary>
    public const string InvalidTransition = "media.invalid_transition";

    /// <summary>Read-back did not return the object the service claims to hold.</summary>
    public const string RestoreFailed = "media.restore_failed";

    /// <summary>The lesson is not a video lesson.</summary>
    public const string NotAVideoLesson = "media.not_a_video_lesson";
}
