using System.Globalization;
using DanielsDojo.Application.Common;
using DanielsDojo.Application.Media;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Media;

namespace DanielsDojo.Infrastructure.Media;

/// <summary>
/// Turns media rows into the shapes the API returns.
/// </summary>
/// <remarks>
/// Kept apart from the service so that the one question this whole workstream exists to answer
/// — is the original safe to delete? — is decided in a single readable place rather than
/// scattered across the endpoints that happen to display it.
/// </remarks>
internal static class MediaProjections
{
    /// <summary>Projects one stored master and its verification record.</summary>
    public static MediaSourceEvidence? ToEvidence(MediaSource? source) =>
        source is null
            ? null
            : new MediaSourceEvidence(
                source.Id,
                source.ContainerName,
                source.BlobName,
                source.ContentLength,
                source.ContentType,
                source.ChecksumSha256,
                source.State.ToString(),
                source.PropertiesVerifiedAtUtc,
                source.RestoreVerifiedAtUtc,
                source.RestoreVerifiedLength);

    /// <summary>Projects one caption track.</summary>
    public static CaptionTrackView ToView(MediaCaptionTrack track) =>
        new(track.Id, track.LanguageCode, track.DisplayName, track.IsDefault, track.Status.ToString());

    /// <summary>
    /// Decides whether every verification step has passed for the master currently serving.
    /// </summary>
    /// <remarks>
    /// Every clause is here because skipping it has a specific failure mode. Properties alone
    /// mean the service lists an object. A restore proves bytes come back. A complete checksum
    /// proves they are the right bytes rather than a truncated upload that happens to have the
    /// expected length recorded. Provider readiness proves the footage survived processing.
    /// Both playback paths prove it is reachable by the people who need it. The human check
    /// proves it is the right footage, which nothing automated can establish.
    /// </remarks>
    public static bool IsSafeToDeleteLocalOriginal(LessonVideo? video, MediaSource? currentSource) =>
        video is not null
        && currentSource is not null
        && currentSource.State == MediaSourceState.Current
        && currentSource.IsCloudVerified
        && currentSource.ChecksumSha256 is { Length: > 0 }
        && video.Status == LessonVideoStatus.Ready
        && video.MuxPlaybackId is { Length: > 0 }
        && video.AdminPlaybackVerifiedAtUtc is not null
        && video.StudentPlaybackVerifiedAtUtc is not null
        && video.HumanSpotCheckAtUtc is not null;

    /// <summary>Projects the whole Admin view of a lesson's video.</summary>
    public static LessonVideoView ToView(
        Guid lessonId,
        LessonVideo? video,
        MediaSource? currentSource,
        MediaSource? incomingSource,
        IReadOnlyList<MediaCaptionTrack> captions)
    {
        if (video is null)
        {
            return new LessonVideoView(
                lessonId,
                VideoId: null,
                Status: "None",
                ProviderMode: ProviderMode.Disabled.ToString(),
                IsPlayable: false,
                DurationSeconds: null,
                AspectRatio: null,
                FailureCode: null,
                CurrentSource: null,
                IncomingSource: null,
                Captions: [],
                Verification: new MediaVerificationEvidence(
                    false, false, false, null, null, null, false),
                RowVersion: null);
        }

        return new LessonVideoView(
            lessonId,
            video.Id,
            video.Status.ToString(),
            video.ProviderMode.ToString(),
            MediaLifecycle.IsPlayable(video.Status) && video.ServablePlaybackId is { Length: > 0 },
            video.DurationSeconds,
            video.AspectRatio,
            video.FailureCode,
            ToEvidence(currentSource),
            ToEvidence(incomingSource),
            [.. captions.Select(ToView)],
            new MediaVerificationEvidence(
                currentSource?.PropertiesVerifiedAtUtc is not null,
                currentSource?.IsCloudVerified == true,
                video.Status == LessonVideoStatus.Ready,
                video.AdminPlaybackVerifiedAtUtc,
                video.StudentPlaybackVerifiedAtUtc,
                video.HumanSpotCheckAtUtc,
                IsSafeToDeleteLocalOriginal(video, currentSource)),
            RowVersionToken.Encode(video.RowVersion));
    }

    /// <summary>
    /// Builds the object name for one upload.
    /// </summary>
    /// <remarks>
    /// The server chooses this, always. A client-supplied name would let one lesson's upload be
    /// aimed at another lesson's object, and the extension is taken from a fixed map rather than
    /// from the submitted file name so a crafted name cannot influence the stored path.
    /// </remarks>
    public static string BuildBlobName(
        MediaPurpose purpose,
        Guid courseId,
        Guid? lessonId,
        Guid sessionId,
        string contentType)
    {
        string folder = purpose switch
        {
            MediaPurpose.LessonVideo => "video",
            MediaPurpose.LessonResource => "resources",
            MediaPurpose.CaptionTrack => "captions",
            MediaPurpose.CourseImage => "images",
            _ => "misc",
        };

        string scope = lessonId is { } lesson
            ? string.Create(CultureInfo.InvariantCulture, $"lessons/{lesson:N}")
            : "course";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"courses/{courseId:N}/{scope}/{folder}/{sessionId:N}{ExtensionFor(contentType)}");
    }

    /// <summary>Accepted master video types, and the extension each is stored with.</summary>
    public static readonly IReadOnlyDictionary<string, string> VideoContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["video/mp4"] = ".mp4",
            ["video/quicktime"] = ".mov",
            ["video/x-matroska"] = ".mkv",
            ["video/webm"] = ".webm",
        };

    /// <summary>Accepted caption types.</summary>
    public static readonly IReadOnlyDictionary<string, string> CaptionContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["text/vtt"] = ".vtt",
            ["application/x-subrip"] = ".srt",
        };

    private static string ExtensionFor(string contentType) =>
        VideoContentTypes.TryGetValue(contentType, out string? video) ? video
        : CaptionContentTypes.TryGetValue(contentType, out string? caption) ? caption
        : ".bin";
}
