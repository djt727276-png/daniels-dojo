namespace DanielsDojo.Application.Catalog;

/// <summary>Query for the Admin course list, which spans every status.</summary>
public sealed record AdminCourseListQuery(string? Search, string? Status, int Page, int PageSize)
{
    /// <summary>Default page size.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Largest page an operator may request.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Trims text and clamps paging.</summary>
    public AdminCourseListQuery Normalized() => new(
        string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
        string.IsNullOrWhiteSpace(Status) ? null : Status.Trim(),
        Page < 1 ? 1 : Page,
        PageSize < 1 ? DefaultPageSize : Math.Min(PageSize, MaxPageSize));
}

/// <summary>Course row in the Admin list.</summary>
public sealed record AdminCourseListItem(
    Guid Id,
    string Slug,
    string Title,
    string Status,
    string Level,
    bool IncludedInMembership,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int SectionCount,
    int LessonCount,
    string RowVersion);

/// <summary>A lesson as the editor sees it, including fields never shown publicly.</summary>
public sealed record AdminLesson(
    Guid Id,
    string Slug,
    string Title,
    string? Summary,
    string LessonType,
    string? BodyMarkdown,
    int SortOrder,
    bool IsPreview,
    string Status,
    int? EstimatedDurationSeconds,
    string? VideoStatus,
    string RowVersion);

/// <summary>A section as the editor sees it.</summary>
public sealed record AdminSection(
    Guid Id,
    string Title,
    string? Description,
    int SortOrder,
    string Status,
    IReadOnlyList<AdminLesson> Lessons,
    string RowVersion);

/// <summary>Full editor detail for one course.</summary>
public sealed record AdminCourseDetail(
    Guid Id,
    string Slug,
    string Title,
    string Summary,
    string Description,
    string Level,
    string Status,
    bool IncludedInMembership,
    string? ImageAltText,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool SlugLocked,
    IReadOnlyList<AdminSection> Sections,
    IReadOnlyList<AdminTag> Tags,
    string RowVersion);

/// <summary>A tag as the editor sees it.</summary>
public sealed record AdminTag(Guid Id, string Name, string NormalizedName);

/// <summary>Creates a Draft course.</summary>
public sealed record CreateCourseRequest(
    string Slug,
    string Title,
    string Summary,
    string Description,
    string Level,
    bool IncludedInMembership);

/// <summary>Updates a course's editable metadata.</summary>
public sealed record UpdateCourseRequest(
    string Slug,
    string Title,
    string Summary,
    string Description,
    string Level,
    bool IncludedInMembership,
    string? ImageAltText,
    string RowVersion);

/// <summary>Creates a Draft section.</summary>
public sealed record CreateSectionRequest(string Title, string? Description);

/// <summary>Updates a section's editable metadata.</summary>
public sealed record UpdateSectionRequest(string Title, string? Description, string RowVersion);

/// <summary>Creates a Draft lesson.</summary>
/// <param name="Slug">
/// Optional. Left empty, the slug is derived from the title, which is what the authoring UI
/// does — an author names a lesson, not a URL segment. Supplied explicitly, it is validated
/// exactly as before, so a caller that needs a specific segment still gets one.
/// </param>
public sealed record CreateLessonRequest(
    string? Slug,
    string Title,
    string? Summary,
    string LessonType,
    string? BodyMarkdown,
    bool IsPreview,
    int? EstimatedDurationSeconds);

/// <summary>Updates a lesson's editable metadata.</summary>
public sealed record UpdateLessonRequest(
    string Slug,
    string Title,
    string? Summary,
    string LessonType,
    string? BodyMarkdown,
    bool IsPreview,
    int? EstimatedDurationSeconds,
    string RowVersion);

/// <summary>
/// A status change. The reason is mandatory and is recorded in the audit trail, so a
/// publication decision is never anonymous.
/// </summary>
public sealed record StatusChangeRequest(string Reason, string RowVersion);

/// <summary>One entry in an exact-set reorder payload.</summary>
public sealed record ReorderItem(Guid Id, string RowVersion);

/// <summary>
/// A complete reorder. The payload must name every non-archived sibling exactly once, in the
/// desired order, so a partial list cannot silently leave gaps.
/// </summary>
public sealed record ReorderRequest(IReadOnlyList<ReorderItem> Items);

/// <summary>Creates a tag with a normalized unique name.</summary>
public sealed record CreateTagRequest(string Name);
