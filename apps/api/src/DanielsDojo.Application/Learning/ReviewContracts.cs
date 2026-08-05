using DanielsDojo.Application.Common;

namespace DanielsDojo.Application.Learning;

/// <summary>One published review as anyone may read it.</summary>
/// <param name="Id">Review identifier.</param>
/// <param name="ReviewerName">The reviewer's display name.</param>
/// <param name="Rating">Stars, 1–5.</param>
/// <param name="Body">The written review.</param>
/// <param name="CreatedAtUtc">When it was written.</param>
/// <param name="EditedAtUtc">When it was last edited, for the edited indicator.</param>
/// <param name="IsMine">Whether the caller wrote it, so the UI offers edit/delete.</param>
public sealed record ReviewView(
    Guid Id,
    string ReviewerName,
    int Rating,
    string Body,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? EditedAtUtc,
    bool IsMine);

/// <summary>A course's review page: honest aggregate plus one page of reviews.</summary>
/// <param name="AverageRating">Mean of published ratings, or null with none.</param>
/// <param name="ReviewCount">Published review count — the only number the average is over.</param>
/// <param name="Reviews">One page, newest first.</param>
/// <param name="TotalCount">Total published reviews for paging.</param>
/// <param name="MyReview">The caller's own review, whatever its state, when signed in.</param>
/// <param name="CanReview">
/// Whether the caller may write one now: full access plus at least one completed lesson.
/// </param>
public sealed record CourseReviews(
    double? AverageRating,
    int ReviewCount,
    IReadOnlyList<ReviewView> Reviews,
    int TotalCount,
    ReviewView? MyReview,
    bool CanReview);

/// <summary>Writing or editing a review.</summary>
/// <param name="Rating">Stars, 1–5.</param>
/// <param name="Body">The written review, up to 4000 characters.</param>
public sealed record WriteReviewRequest(int Rating, string Body);

/// <summary>One review in the moderation queue.</summary>
/// <param name="Id">Review identifier.</param>
/// <param name="CourseTitle">Course it belongs to.</param>
/// <param name="ReviewerName">Author display name.</param>
/// <param name="Rating">Stars.</param>
/// <param name="Body">Content under review.</param>
/// <param name="Status">Current lifecycle state.</param>
/// <param name="ModerationReason">Why it was hidden, when it was.</param>
/// <param name="CreatedAtUtc">When it was written.</param>
public sealed record ModerationReviewView(
    Guid Id,
    string CourseTitle,
    string ReviewerName,
    int Rating,
    string Body,
    string Status,
    string? ModerationReason,
    DateTimeOffset CreatedAtUtc);

/// <summary>Stable error codes for reviews.</summary>
public static class ReviewErrorCodes
{
    /// <summary>The caller does not hold the course.</summary>
    public const string NotEntitled = "reviews.not_entitled";

    /// <summary>The caller has not completed enough of the course to review it.</summary>
    public const string ProgressRequired = "reviews.progress_required";
}

/// <summary>Course reviews: reading, writing, and moderating.</summary>
public interface ICourseReviewService
{
    /// <summary>One page of a course's published reviews plus the honest aggregate.</summary>
    Task<OperationResult<CourseReviews>> GetCourseReviewsAsync(
        string courseSlug,
        Guid? userId,
        int page,
        CancellationToken cancellationToken = default);

    /// <summary>Creates or updates the caller's review. The eligibility gate lives here.</summary>
    Task<OperationResult<ReviewView>> WriteReviewAsync(
        Guid userId,
        string courseSlug,
        WriteReviewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Withdraws the caller's own review. A tombstone remains; the slot stays used.</summary>
    Task<OperationResult<bool>> DeleteReviewAsync(
        Guid userId,
        string courseSlug,
        CancellationToken cancellationToken = default);

    /// <summary>Moderation queue, filterable by status.</summary>
    Task<OperationResult<IReadOnlyList<ModerationReviewView>>> ListForModerationAsync(
        string? status,
        CancellationToken cancellationToken = default);

    /// <summary>Hides a review from the public and the aggregate. Reason mandatory.</summary>
    Task<OperationResult<ModerationReviewView>> HideReviewAsync(
        Guid reviewId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>Restores a hidden review to publication.</summary>
    Task<OperationResult<ModerationReviewView>> RestoreReviewAsync(
        Guid reviewId,
        CancellationToken cancellationToken = default);
}
