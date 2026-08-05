using DanielsDojo.Application.Common;
using DanielsDojo.Application.Learning;
using DanielsDojo.Application.Media;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Media;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DanielsDojo.Infrastructure.Media;

/// <summary>
/// Issues viewing authorisations to students.
/// </summary>
/// <remarks>
/// <para>
/// Access is decided by <see cref="ICourseAccessEvaluator"/> and nowhere else. This service
/// knows how to mint a token; it has no opinion at all about who deserves one, which is what
/// keeps a future change to the access rules from having to be remembered here.
/// </para>
/// <para>
/// A token is minted per request and never cached, stored, or logged. Sharing a response body
/// therefore hands somebody a few minutes of playback rather than a permanent key to paid
/// course video.
/// </para>
/// </remarks>
internal sealed class LessonPlaybackService(
    DanielsDojoDbContext context,
    ICourseAccessEvaluator access,
    IVideoPipeline video,
    IOptions<VideoProviderOptions> options,
    TimeProvider timeProvider) : ILessonPlaybackService
{
    private readonly VideoProviderOptions _options = options.Value;

    public async Task<OperationResult<LessonPlaybackGrant>> GetPlaybackAsync(
        Guid? userId,
        Guid lessonId,
        CancellationToken cancellationToken = default)
    {
        CourseAccess decision = await access.EvaluateLessonAsync(userId, lessonId, cancellationToken);

        if (!decision.Granted)
        {
            return Refuse(decision);
        }

        LessonVideo? record = await context.LessonVideos
            .Include(candidate => candidate.CaptionTracks)
            .FirstOrDefaultAsync(candidate => candidate.LessonId == lessonId, cancellationToken);

        if (record is null || record.ServablePlaybackId is not { Length: > 0 } playbackId)
        {
            return OperationResult.Conflict(
                MediaErrorCodes.NotReady,
                "This lesson's video is not ready to play yet.")
                .ToFailure<LessonPlaybackGrant>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset expiresAt = now.AddMinutes(_options.PlaybackTokenMinutes);

        // Recorded once. This is evidence that the student-facing path works end to end, and it
        // is one of the checks that has to pass before an original is safe to remove locally.
        if (record.StudentPlaybackVerifiedAtUtc is null
            && decision.Reason != CourseAccessReason.AdminPreview)
        {
            record.StudentPlaybackVerifiedAtUtc = now;
            record.UpdatedAtUtc = now;

            await context.SaveChangesAsync(cancellationToken);
        }

        // A preview viewer sees the video and nothing else; captions ride along with the
        // lesson, so they follow the same rule as the rest of the materials.
        IReadOnlyList<CaptionTrackView> captions = decision.AllowsResourceDownload
            ? [.. record.CaptionTracks
                .Where(track => track.Status == LessonVideoStatus.Ready)
                .Select(MediaProjections.ToView)]
            : [];

        return OperationResult.FromValue(new LessonPlaybackGrant(
            lessonId,
            playbackId,
            video.UsesSignedPlayback ? video.CreatePlaybackToken(playbackId, expiresAt) : null,
            expiresAt,
            record.DurationSeconds,
            record.AspectRatio,
            captions,
            decision.Reason.ToString()));
    }

    /// <summary>
    /// Turns an access refusal into the outcome the endpoint maps to a status code.
    /// </summary>
    /// <remarks>
    /// A lesson the viewer may not know exists is reported as not found rather than forbidden,
    /// so an unpublished course cannot be enumerated by watching which identifiers answer 403.
    /// </remarks>
    private static OperationResult<LessonPlaybackGrant> Refuse(CourseAccess decision) =>
        decision.Denial switch
        {
            CourseAccessDenial.NotFound or CourseAccessDenial.NotPublished =>
                OperationResult.NotFound().ToFailure<LessonPlaybackGrant>(),

            CourseAccessDenial.AuthenticationRequired =>
                OperationResult.Forbidden(
                    decision.Code,
                    "Sign in to watch this lesson.")
                    .ToFailure<LessonPlaybackGrant>(),

            _ => OperationResult.Forbidden(
                decision.Code,
                "This lesson is not included in your current access.")
                .ToFailure<LessonPlaybackGrant>(),
        };
}
