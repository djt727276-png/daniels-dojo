using DanielsDojo.Application.Catalog;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DanielsDojo.Infrastructure.Catalog;

/// <summary>
/// SQL projections for the public catalog.
/// </summary>
/// <remarks>
/// Every read is no-tracking and projects straight into contract records, so entities are never
/// serialised and a row version, storage key, provider identifier, or audit field cannot leak
/// by accident — those columns are simply never selected.
/// <para>
/// Price resolution happens once per request over a bounded set rather than per course, so the
/// list endpoint issues a fixed number of queries regardless of page size.
/// </para>
/// </remarks>
public sealed partial class PublicCatalogQueries(
    DanielsDojoDbContext context,
    TimeProvider timeProvider,
    ILogger<PublicCatalogQueries> logger) : IPublicCatalogQueries
{
    /// <inheritdoc />
    public async Task<PagedResult<CourseCard>> ListCoursesAsync(
        CourseListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        CourseListQuery normalized = query.Normalized();

        IQueryable<Course> courses = context.Courses
            .AsNoTracking()
            .Where(course => course.Status == PublicationStatus.Published);

        if (normalized.Search is { } search)
        {
            courses = courses.Where(course =>
                EF.Functions.Like(course.Title, $"%{search}%")
                || EF.Functions.Like(course.Summary, $"%{search}%"));
        }

        if (normalized.Level is { } level
            && Enum.TryParse(level, ignoreCase: true, out CourseLevel parsedLevel))
        {
            courses = courses.Where(course => course.Level == parsedLevel);
        }
        else if (normalized.Level is not null)
        {
            // An unrecognised level matches nothing rather than being silently ignored, so a
            // client cannot widen its own results with a typo.
            courses = courses.Where(_ => false);
        }

        if (normalized.Tag is { } tag)
        {
            courses = courses.Where(course => context.CourseTags
                .Any(courseTag => courseTag.CourseId == course.Id
                    && context.Tags.Any(t => t.Id == courseTag.TagId && t.NormalizedName == tag)));
        }

        int totalCount = await courses.CountAsync(cancellationToken).ConfigureAwait(false);

        // Deterministic ordering: newest first, then title, then ID as the final tie-break so
        // paging can never repeat or skip a row.
        var page = await courses
            .OrderByDescending(course => course.PublishedAtUtc)
            .ThenBy(course => course.Title)
            .ThenBy(course => course.Id)
            .Skip((normalized.Page - 1) * normalized.PageSize)
            .Take(normalized.PageSize)
            .Select(course => new
            {
                course.Id,
                course.Slug,
                course.Title,
                course.Summary,
                course.Level,
                course.IncludedInMembership,
                course.PublishedAtUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Guid[] courseIds = [.. page.Select(course => course.Id)];

        Dictionary<Guid, List<string>> tagsByCourse =
            await LoadTagsAsync(courseIds, cancellationToken).ConfigureAwait(false);

        PublicPrice? membershipPrice =
            await ResolveMembershipPriceAsync(cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, PublicPrice> lifetimeByCourse =
            await ResolveLifetimePricesAsync(courseIds, cancellationToken).ConfigureAwait(false);

        List<CourseCard> items = [.. page.Select(course => new CourseCard(
            course.Slug,
            course.Title,
            course.Summary,
            course.Level.ToString(),
            course.IncludedInMembership,
            course.PublishedAtUtc,
            tagsByCourse.TryGetValue(course.Id, out List<string>? tags) ? tags : [],
            course.IncludedInMembership ? membershipPrice : null,
            lifetimeByCourse.GetValueOrDefault(course.Id)))];

        int totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)normalized.PageSize);

        return new PagedResult<CourseCard>(
            items, normalized.Page, normalized.PageSize, totalCount, totalPages);
    }

    /// <inheritdoc />
    public async Task<CourseDetail?> GetCourseAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var course = await context.Courses
            .AsNoTracking()
            .Where(candidate => candidate.Slug == slug
                && candidate.Status == PublicationStatus.Published)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Slug,
                candidate.Title,
                candidate.Summary,
                candidate.Description,
                candidate.Level,
                candidate.IncludedInMembership,
                candidate.PublishedAtUtc,
                candidate.ImageAltText,
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return null;
        }

        // Only published sections and lessons appear, in stored order.
        var sections = await context.CourseSections
            .AsNoTracking()
            .Where(section => section.CourseId == course.Id
                && section.Status == PublicationStatus.Published)
            .OrderBy(section => section.SortOrder)
            .Select(section => new
            {
                section.Id,
                section.Title,
                section.Description,
                Lessons = context.Lessons
                    .Where(lesson => lesson.CourseSectionId == section.Id
                        && lesson.Status == PublicationStatus.Published)
                    .OrderBy(lesson => lesson.SortOrder)
                    .Select(lesson => new CourseOutlineLesson(
                        lesson.Slug,
                        lesson.Title,
                        lesson.Summary,
                        lesson.LessonType.ToString(),
                        lesson.IsPreview,
                        lesson.EstimatedDurationSeconds))
                    .ToList(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, List<string>> tags =
            await LoadTagsAsync([course.Id], cancellationToken).ConfigureAwait(false);

        PublicPrice? membershipPrice = course.IncludedInMembership
            ? await ResolveMembershipPriceAsync(cancellationToken).ConfigureAwait(false)
            : null;

        Dictionary<Guid, PublicPrice> lifetime =
            await ResolveLifetimePricesAsync([course.Id], cancellationToken).ConfigureAwait(false);

        return new CourseDetail(
            course.Slug,
            course.Title,
            course.Summary,
            course.Description,
            course.Level.ToString(),
            course.IncludedInMembership,
            course.PublishedAtUtc,
            course.ImageAltText,
            tags.TryGetValue(course.Id, out List<string>? courseTags) ? courseTags : [],
            [.. sections.Select(section => new CourseOutlineSection(
                section.Title, section.Description, section.Lessons))],
            membershipPrice,
            lifetime.GetValueOrDefault(course.Id));
    }

    /// <inheritdoc />
    public Task<LessonPreview?> GetLessonPreviewAsync(
        string courseSlug,
        string lessonSlug,
        CancellationToken cancellationToken = default)
    {
        // Every gate is in the predicate: the course, its section, and the lesson must all be
        // Published, the lesson must be a preview, and it must be an Article. Anything else
        // yields null, which the endpoint turns into the same 404 as a missing course.
        return context.Lessons
            .AsNoTracking()
            .Where(lesson =>
                lesson.Slug == lessonSlug
                && lesson.IsPreview
                && lesson.LessonType == LessonType.Article
                && lesson.Status == PublicationStatus.Published
                && lesson.BodyMarkdown != null
                && context.Courses.Any(course =>
                    course.Id == lesson.CourseId
                    && course.Slug == courseSlug
                    && course.Status == PublicationStatus.Published)
                && context.CourseSections.Any(section =>
                    section.Id == lesson.CourseSectionId
                    && section.Status == PublicationStatus.Published))
            .Select(lesson => new LessonPreview(
                courseSlug,
                context.Courses.Where(course => course.Id == lesson.CourseId)
                    .Select(course => course.Title)
                    .First(),
                lesson.Slug,
                lesson.Title,
                lesson.Summary,
                lesson.BodyMarkdown!))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, List<string>>> LoadTagsAsync(
        Guid[] courseIds,
        CancellationToken cancellationToken)
    {
        if (courseIds.Length == 0)
        {
            return [];
        }

        var rows = await context.CourseTags
            .AsNoTracking()
            .Where(courseTag => courseIds.Contains(courseTag.CourseId))
            .Join(
                context.Tags.AsNoTracking(),
                courseTag => courseTag.TagId,
                tag => tag.Id,
                (courseTag, tag) => new { courseTag.CourseId, tag.Name })
            .OrderBy(row => row.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(row => row.CourseId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Name).ToList());
    }

    /// <summary>
    /// Resolves the single current membership price.
    /// </summary>
    /// <remarks>
    /// "Current" means an Active price on an Active Membership offer whose effective window
    /// covers now. If more than one qualifies the newest by effective time then ID wins, and
    /// the situation is logged as a data-quality problem — it means someone published
    /// overlapping prices, which an operator needs to fix even though the public response
    /// stays deterministic.
    /// </remarks>
    private async Task<PublicPrice?> ResolveMembershipPriceAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        // Filters stay on the entities; the record is constructed last. EF cannot translate a
        // predicate applied to an already-projected positional record.
        List<PriceRow> candidates = await CurrentPriceQuery(now, OfferKind.Membership)
            .OrderByDescending(pair => pair.Price.EffectiveFromUtc)
            .ThenByDescending(pair => pair.Price.Id)
            .Select(pair => new PriceRow(
                pair.Price.Id,
                pair.Offer.CourseId,
                pair.Price.AmountMinor,
                pair.Price.Currency,
                pair.Price.BillingInterval,
                pair.Price.EffectiveFromUtc))
            .Take(2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count > 1)
        {
            LogAmbiguousMembershipPrice(logger, candidates[0].PriceId);
        }

        return candidates[0].ToPublicPrice();
    }

    /// <summary>Resolves the current lifetime price for each requested course.</summary>
    private async Task<Dictionary<Guid, PublicPrice>> ResolveLifetimePricesAsync(
        Guid[] courseIds,
        CancellationToken cancellationToken)
    {
        if (courseIds.Length == 0)
        {
            return [];
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        List<PriceRow> rows = await CurrentPriceQuery(now, OfferKind.CourseLifetime)
            .Where(pair => pair.Offer.CourseId != null
                && courseIds.Contains(pair.Offer.CourseId!.Value))
            .Select(pair => new PriceRow(
                pair.Price.Id,
                pair.Offer.CourseId,
                pair.Price.AmountMinor,
                pair.Price.Currency,
                pair.Price.BillingInterval,
                pair.Price.EffectiveFromUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, PublicPrice> result = [];

        foreach (IGrouping<Guid, PriceRow> group in rows.GroupBy(row => row.CourseId!.Value))
        {
            PriceRow[] ordered = [.. group
                .OrderByDescending(row => row.EffectiveFromUtc)
                .ThenByDescending(row => row.PriceId)];

            if (ordered.Length > 1)
            {
                LogAmbiguousLifetimePrice(logger, group.Key);
            }

            result[group.Key] = ordered[0].ToPublicPrice();
        }

        return result;
    }

    /// <summary>
    /// Active prices on active offers whose effective window covers <paramref name="now"/>.
    /// Retirement is respected, so a withdrawn price never shows publicly.
    /// </summary>
    private IQueryable<PriceOfferPair> CurrentPriceQuery(DateTimeOffset now, OfferKind kind) =>
        from price in context.Prices.AsNoTracking()
        join offer in context.Offers.AsNoTracking() on price.OfferId equals offer.Id
        where offer.Kind == kind
            && price.Status == CommerceStatus.Active
            && offer.Status == CommerceStatus.Active
            && price.EffectiveFromUtc <= now
            && (price.RetiredAtUtc == null || price.RetiredAtUtc > now)
        select new PriceOfferPair { Price = price, Offer = offer };

    /// <summary>Join shape used only to keep filters on entity properties.</summary>
    private sealed class PriceOfferPair
    {
        public required Price Price { get; init; }

        public required Offer Offer { get; init; }
    }

    /// <summary>Flat projection so no entity is materialised for pricing.</summary>
    private sealed record PriceRow(
        Guid PriceId,
        Guid? CourseId,
        long AmountMinor,
        string Currency,
        BillingInterval BillingInterval,
        DateTimeOffset EffectiveFromUtc)
    {
        public PublicPrice ToPublicPrice() =>
            new(AmountMinor, Currency, BillingInterval.ToString());
    }

    // Logs the condition without exposing it publicly; the response stays deterministic.
    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Warning,
        Message = "Multiple current membership prices are active. Using price {PriceId}. An operator should retire the duplicates.")]
    private static partial void LogAmbiguousMembershipPrice(ILogger logger, Guid priceId);

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Warning,
        Message = "Multiple current lifetime prices are active for course {CourseId}. Using the most recent. An operator should retire the duplicates.")]
    private static partial void LogAmbiguousLifetimePrice(ILogger logger, Guid courseId);
}
