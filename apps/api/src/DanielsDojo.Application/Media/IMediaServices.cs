using DanielsDojo.Application.Common;

namespace DanielsDojo.Application.Media;

/// <summary>
/// The Admin media pipeline: authorise an upload, verify what landed, process it, and record
/// the evidence that says the original is safe to remove.
/// </summary>
/// <remarks>
/// Every method here is an audited administrative action. None of them can delete anything —
/// the strongest thing a replacement can do is mark the previous master superseded, which
/// leaves it in place and on record.
/// </remarks>
public interface IAdminMediaService
{
    /// <summary>Authorises one upload of a lesson's master video.</summary>
    Task<OperationResult<MediaUploadTicket>> RequestLessonVideoUploadAsync(
        Guid lessonId,
        MediaUploadRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Authorises one upload of a caption track for a lesson's video.</summary>
    Task<OperationResult<MediaUploadTicket>> RequestCaptionUploadAsync(
        Guid lessonId,
        string languageCode,
        MediaUploadRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms an upload actually landed, records the verified source, and starts processing.
    /// </summary>
    /// <remarks>
    /// The client saying "done" is not evidence. This reads the object back from storage and
    /// compares it against what was authorised before anything downstream trusts it.
    /// </remarks>
    Task<OperationResult<LessonVideoView>> CompleteUploadAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the current state and evidence for a lesson's video.</summary>
    Task<OperationResult<LessonVideoView>> GetLessonVideoAsync(
        Guid lessonId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads bytes back out of storage and records the result. This is the check that
    /// distinguishes "the service lists an object" from "the object comes back".
    /// </summary>
    Task<OperationResult<LessonVideoView>> VerifyRestoreAsync(
        Guid lessonId,
        CancellationToken cancellationToken = default);

    /// <summary>Issues an administrator preview and records that playback succeeded.</summary>
    Task<OperationResult<LessonPlaybackGrant>> PreviewAsync(
        Guid lessonId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a human watched the processed video and confirmed it is the right footage.
    /// </summary>
    Task<OperationResult<LessonVideoView>> RecordSpotCheckAsync(
        Guid lessonId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Repairs recorded state from the provider for videos whose notifications never arrived.
    /// </summary>
    Task<OperationResult<MediaReconciliationReport>> ReconcileAsync(
        Guid? lessonId,
        CancellationToken cancellationToken = default);
}

/// <summary>Inbound provider notifications.</summary>
public interface IMediaWebhookService
{
    /// <summary>
    /// Verifies and applies one notification. Returns true when the delivery was accepted,
    /// including when it was a duplicate or arrived out of order — the provider should not
    /// retry something that was understood and deliberately ignored.
    /// </summary>
    Task<bool> HandleVideoEventAsync(
        string payload,
        string? signatureHeader,
        CancellationToken cancellationToken = default);
}

/// <summary>Playback for viewers, gated by the course access evaluator.</summary>
public interface ILessonPlaybackService
{
    /// <summary>
    /// Issues a viewing authorisation for one lesson, or explains why not.
    /// </summary>
    /// <param name="userId">The signed-in viewer, or null for an anonymous one.</param>
    Task<OperationResult<LessonPlaybackGrant>> GetPlaybackAsync(
        Guid? userId,
        Guid lessonId,
        CancellationToken cancellationToken = default);
}
