namespace DanielsDojo.Application.Catalog;

/// <summary>A page of results with the information a client needs to page further.</summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Items">Items on this page.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Requested page size after clamping.</param>
/// <param name="TotalCount">Total matching items.</param>
/// <param name="TotalPages">Total pages at this page size.</param>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

/// <summary>Query for the public course list.</summary>
/// <param name="Search">Free text matched against title and summary.</param>
/// <param name="Level">Optional difficulty filter.</param>
/// <param name="Tag">Optional normalized tag filter.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Items per page.</param>
public sealed record CourseListQuery(
    string? Search,
    string? Level,
    string? Tag,
    int Page,
    int PageSize)
{
    /// <summary>Default page size when the caller does not choose one.</summary>
    public const int DefaultPageSize = 12;

    /// <summary>Largest page a caller may request.</summary>
    public const int MaxPageSize = 48;

    /// <summary>
    /// Normalises user input: trims text, clamps paging, and upper-cases the tag so it matches
    /// the stored normalized form.
    /// </summary>
    public CourseListQuery Normalized() => new(
        Search: string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
        Level: string.IsNullOrWhiteSpace(Level) ? null : Level.Trim(),
        Tag: string.IsNullOrWhiteSpace(Tag) ? null : Tag.Trim().ToUpperInvariant(),
        Page: Page < 1 ? 1 : Page,
        PageSize: PageSize < 1 ? DefaultPageSize : Math.Min(PageSize, MaxPageSize));
}

/// <summary>
/// A price as the public API presents it: integer minor units plus currency and interval, so
/// no amount is ever hard-coded in a client.
/// </summary>
/// <param name="AmountMinor">Amount in minor units.</param>
/// <param name="Currency">Uppercase ISO-4217 code.</param>
/// <param name="Interval">Billing interval name.</param>
public sealed record PublicPrice(long AmountMinor, string Currency, string Interval);

/// <summary>Card-level course summary for the catalog list.</summary>
public sealed record CourseCard(
    string Slug,
    string Title,
    string Summary,
    string Level,
    bool IncludedInMembership,
    DateTimeOffset? PublishedAtUtc,
    IReadOnlyList<string> Tags,
    PublicPrice? MembershipPrice,
    PublicPrice? LifetimePrice);

/// <summary>One lesson in the public outline. Never carries body content.</summary>
public sealed record CourseOutlineLesson(
    string Slug,
    string Title,
    string? Summary,
    string LessonType,
    bool IsPreview,
    int? EstimatedDurationSeconds);

/// <summary>One section in the public outline.</summary>
public sealed record CourseOutlineSection(
    string Title,
    string? Description,
    IReadOnlyList<CourseOutlineLesson> Lessons);

/// <summary>Full public course detail.</summary>
public sealed record CourseDetail(
    string Slug,
    string Title,
    string Summary,
    string Description,
    string Level,
    bool IncludedInMembership,
    DateTimeOffset? PublishedAtUtc,
    string? ImageAltText,
    IReadOnlyList<string> Tags,
    IReadOnlyList<CourseOutlineSection> Sections,
    PublicPrice? MembershipPrice,
    PublicPrice? LifetimePrice);

/// <summary>
/// A preview lesson's stored text. The body is plain text; nothing renders it as HTML.
/// </summary>
public sealed record LessonPreview(
    string CourseSlug,
    string CourseTitle,
    string LessonSlug,
    string Title,
    string? Summary,
    string Body);
