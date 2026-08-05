using DanielsDojo.Application.Common;
using DanielsDojo.Application.Learning;
using DanielsDojo.Domain.Learning;
using DanielsDojo.Infrastructure.Auditing;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DanielsDojo.Infrastructure.Learning;

/// <summary>
/// Course reviews.
/// </summary>
/// <remarks>
/// <para>
/// The eligibility gate is the whole point: a review may only be written by somebody who
/// holds full access to the course <em>and</em> has completed at least one of its published
/// lessons. That threshold is defined here, once — it keeps drive-by ratings out without
/// demanding the whole course, and both the API and the UI read the same answer through
/// <see cref="ICourseReviewService.GetCourseReviewsAsync"/>.
/// </para>
/// <para>
/// Aggregates are computed from published rows at read time. There is no stored average to
/// go stale, so hiding a review corrects the number in the same transaction.
/// </para>
/// </remarks>
internal sealed class CourseReviewService : ICourseReviewService
{
    private const int PageSize = 10;

    private readonly DanielsDojoDbContext context;
    private readonly ICourseAccessEvaluator access;
    private readonly TimeProvider timeProvider;
    private readonly AuditTrail audit;

    public CourseReviewService(
        DanielsDojoDbContext context,
        ICourseAccessEvaluator access,
        IOperationContext operationContext,
        TimeProvider timeProvider)
    {
        this.context = context;
        this.access = access;
        this.timeProvider = timeProvider;

        audit = new AuditTrail(context, operationContext, timeProvider);
    }

    public async Task<OperationResult<CourseReviews>> GetCourseReviewsAsync(
        string courseSlug,
        Guid? userId,
        int page,
        CancellationToken cancellationToken = default)
    {
        Guid? courseId = await context.Courses
            .AsNoTracking()
            .Where(course => course.Slug == courseSlug
                && course.Status == Domain.Catalog.PublicationStatus.Published)
            .Select(course => (Guid?)course.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (courseId is not { } id)
        {
            return OperationResult.NotFound().ToFailure<CourseReviews>();
        }

        IQueryable<CourseReview> published = context.CourseReviews
            .AsNoTracking()
            .Where(review => review.CourseId == id
                && review.Status == CourseReviewStatus.Published);

        int total = await published.CountAsync(cancellationToken);
        double? average = total == 0
            ? null
            : Math.Round(await published.AverageAsync(review => (double)review.Rating, cancellationToken), 1);

        int skip = Math.Max(page, 0) * PageSize;

        List<ReviewView> reviews = await published
            .OrderByDescending(review => review.CreatedAtUtc)
            .Skip(skip)
            .Take(PageSize)
            .Select(review => new ReviewView(
                review.Id,
                review.User!.DisplayName,
                review.Rating,
                review.Body,
                review.CreatedAtUtc,
                review.EditedAtUtc,
                userId != null && review.UserId == userId))
            .ToListAsync(cancellationToken);

        ReviewView? mine = null;
        bool canReview = false;

        if (userId is { } caller)
        {
            mine = await context.CourseReviews
                .AsNoTracking()
                .Where(review => review.CourseId == id
                    && review.UserId == caller
                    && review.Status != CourseReviewStatus.Deleted)
                .Select(review => new ReviewView(
                    review.Id,
                    review.User!.DisplayName,
                    review.Rating,
                    review.Body,
                    review.CreatedAtUtc,
                    review.EditedAtUtc,
                    true))
                .FirstOrDefaultAsync(cancellationToken);

            canReview = mine is null && await IsEligibleAsync(caller, id, cancellationToken);
        }

        return OperationResult.FromValue(new CourseReviews(
            average, total, reviews, total, mine, canReview));
    }

    public async Task<OperationResult<ReviewView>> WriteReviewAsync(
        Guid userId,
        string courseSlug,
        WriteReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid? resolved = await ResolveCourseAsync(courseSlug, cancellationToken);

        if (resolved is not { } courseId)
        {
            return OperationResult.NotFound().ToFailure<ReviewView>();
        }

        if (request.Rating is < 1 or > 5)
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed, "rating", "A rating is one to five stars.")
                .ToFailure<ReviewView>();
        }

        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 4000)
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "body",
                "Write something about the course, up to 4000 characters.")
                .ToFailure<ReviewView>();
        }

        // The gate. Entitlement first, then the progress threshold, so the refusal says
        // which one actually failed.
        CourseAccess decision = await access.EvaluateCourseAsync(userId, courseId, cancellationToken);

        if (!decision.Granted || decision.IsPreviewOnly)
        {
            return OperationResult.Forbidden(
                ReviewErrorCodes.NotEntitled,
                "Reviews come from people who hold the course.")
                .ToFailure<ReviewView>();
        }

        if (!await HasCompletedALessonAsync(userId, courseId, cancellationToken))
        {
            return OperationResult.Forbidden(
                ReviewErrorCodes.ProgressRequired,
                "Complete at least one lesson before reviewing the course.")
                .ToFailure<ReviewView>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        CourseReview? existing = await context.CourseReviews
            .FirstOrDefaultAsync(
                review => review.UserId == userId && review.CourseId == courseId,
                cancellationToken);

        if (existing is null)
        {
            existing = new CourseReview
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                CourseId = courseId,
                Rating = request.Rating,
                Body = request.Body.Trim(),
                Status = CourseReviewStatus.Published,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            context.CourseReviews.Add(existing);
        }
        else
        {
            if (existing.Status == CourseReviewStatus.Hidden)
            {
                // A moderated review is not overwritten by its author; that would be an
                // edit-past-the-moderator loophole.
                return OperationResult.Conflict(
                    ReviewErrorCodes.NotEntitled,
                    "This review is under moderation and cannot be edited.")
                    .ToFailure<ReviewView>();
            }

            existing.Rating = request.Rating;
            existing.Body = request.Body.Trim();
            existing.Status = CourseReviewStatus.Published;
            existing.EditedAtUtc = now;
            existing.UpdatedAtUtc = now;
        }

        await context.SaveChangesAsync(cancellationToken);

        string reviewerName = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.DisplayName)
            .SingleAsync(cancellationToken);

        return OperationResult.FromValue(new ReviewView(
            existing.Id,
            reviewerName,
            existing.Rating,
            existing.Body,
            existing.CreatedAtUtc,
            existing.EditedAtUtc,
            true));
    }

    public async Task<OperationResult<bool>> DeleteReviewAsync(
        Guid userId,
        string courseSlug,
        CancellationToken cancellationToken = default)
    {
        Guid? resolved = await ResolveCourseAsync(courseSlug, cancellationToken);

        if (resolved is not { } courseId)
        {
            return OperationResult.NotFound().ToFailure<bool>();
        }

        CourseReview? review = await context.CourseReviews
            .FirstOrDefaultAsync(
                candidate => candidate.UserId == userId && candidate.CourseId == courseId,
                cancellationToken);

        if (review is null || review.Status == CourseReviewStatus.Deleted)
        {
            return OperationResult.NotFound().ToFailure<bool>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        review.Status = CourseReviewStatus.Deleted;
        review.ModerationReason = null;
        review.UpdatedAtUtc = now;

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.FromValue(true);
    }

    public async Task<OperationResult<IReadOnlyList<ModerationReviewView>>> ListForModerationAsync(
        string? status,
        CancellationToken cancellationToken = default)
    {
        IQueryable<CourseReview> query = context.CourseReviews.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            // An unrecognised filter matches nothing rather than everything.
            query = Enum.TryParse(status, ignoreCase: true, out CourseReviewStatus parsed)
                ? query.Where(review => review.Status == parsed)
                : query.Where(static _ => false);
        }

        List<ModerationReviewView> reviews = await query
            .OrderByDescending(review => review.UpdatedAtUtc)
            .Take(100)
            .Select(review => new ModerationReviewView(
                review.Id,
                review.Course!.Title,
                review.User!.DisplayName,
                review.Rating,
                review.Body,
                review.Status.ToString(),
                review.ModerationReason,
                review.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return OperationResult.FromValue<IReadOnlyList<ModerationReviewView>>(reviews);
    }

    public async Task<OperationResult<ModerationReviewView>> HideReviewAsync(
        Guid reviewId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed, "reason", "Hiding a review must say why.")
                .ToFailure<ModerationReviewView>();
        }

        CourseReview? review = await context.CourseReviews
            .FirstOrDefaultAsync(candidate => candidate.Id == reviewId, cancellationToken);

        if (review is null)
        {
            return OperationResult.NotFound().ToFailure<ModerationReviewView>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        review.Status = CourseReviewStatus.Hidden;
        review.ModerationReason = reason.Trim();
        review.UpdatedAtUtc = now;

        audit.Append(
            "Reviews.Hidden",
            nameof(CourseReview),
            review.Id,
            reason: reason.Trim());

        await context.SaveChangesAsync(cancellationToken);

        return await ModerationViewAsync(review.Id, cancellationToken);
    }

    public async Task<OperationResult<ModerationReviewView>> RestoreReviewAsync(
        Guid reviewId,
        CancellationToken cancellationToken = default)
    {
        CourseReview? review = await context.CourseReviews
            .FirstOrDefaultAsync(candidate => candidate.Id == reviewId, cancellationToken);

        if (review is null)
        {
            return OperationResult.NotFound().ToFailure<ModerationReviewView>();
        }

        if (review.Status == CourseReviewStatus.Hidden)
        {
            review.Status = CourseReviewStatus.Published;
            review.ModerationReason = null;
            review.UpdatedAtUtc = timeProvider.GetUtcNow();

            audit.Append("Reviews.Restored", nameof(CourseReview), review.Id);

            await context.SaveChangesAsync(cancellationToken);
        }

        return await ModerationViewAsync(review.Id, cancellationToken);
    }

    private Task<Guid?> ResolveCourseAsync(string courseSlug, CancellationToken cancellationToken) =>
        context.Courses
            .AsNoTracking()
            .Where(course => course.Slug == courseSlug
                && course.Status == Domain.Catalog.PublicationStatus.Published)
            .Select(course => (Guid?)course.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The review-eligibility rule, in one place: full access and at least one completed
    /// published lesson of the course.
    /// </summary>
    private async Task<bool> IsEligibleAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken)
    {
        CourseAccess decision = await access.EvaluateCourseAsync(userId, courseId, cancellationToken);

        return decision.Granted
            && !decision.IsPreviewOnly
            && await HasCompletedALessonAsync(userId, courseId, cancellationToken);
    }

    private Task<bool> HasCompletedALessonAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken) =>
        context.LessonProgress
            .AsNoTracking()
            .AnyAsync(
                entry => entry.UserId == userId
                    && entry.CompletedAtUtc != null
                    && entry.Lesson!.CourseId == courseId,
                cancellationToken);

    private async Task<OperationResult<ModerationReviewView>> ModerationViewAsync(
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        ModerationReviewView view = await context.CourseReviews
            .AsNoTracking()
            .Where(review => review.Id == reviewId)
            .Select(review => new ModerationReviewView(
                review.Id,
                review.Course!.Title,
                review.User!.DisplayName,
                review.Rating,
                review.Body,
                review.Status.ToString(),
                review.ModerationReason,
                review.CreatedAtUtc))
            .SingleAsync(cancellationToken);

        return OperationResult.FromValue(view);
    }
}
