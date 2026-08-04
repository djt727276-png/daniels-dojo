using System.Globalization;
using DanielsDojo.Application.Catalog;
using DanielsDojo.Application.Common;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Infrastructure.Auditing;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DanielsDojo.Infrastructure.Catalog;

/// <summary>
/// Authoring operations over the catalog.
/// </summary>
/// <remarks>
/// <para>
/// Three rules run through every method here. Every write carries the caller's opaque row
/// version, so a second author editing the same record is told to reload rather than silently
/// overwriting the first. Every status change carries an operator reason, which is written to
/// the audit trail by the same <c>SaveChanges</c> as the change itself. And publication never
/// cascades: publishing a course does not publish its sections, so nothing reaches students
/// because a parent was approved.
/// </para>
/// <para>
/// Reordering renumbers in two passes inside one transaction. The unique index on
/// (parent, sort order) means a straight renumber would collide with itself part-way through,
/// so positions are first parked in a high range and then written down to their final values.
/// </para>
/// </remarks>
internal sealed class AdminCatalogService : IAdminCatalogService
{
    /// <summary>
    /// Temporary offset used by the first reorder pass. Well above any realistic position, so
    /// the parked values cannot collide with a sibling that is keeping its place.
    /// </summary>
    private const int ReorderParkingOffset = 1_000_000;

    /// <summary>Hard ceiling on the tag list, which has no paging of its own.</summary>
    private const int MaxTagListSize = 500;

    private readonly DanielsDojoDbContext context;
    private readonly TimeProvider timeProvider;
    private readonly AuditTrail audit;

    public AdminCatalogService(
        DanielsDojoDbContext context,
        IOperationContext operationContext,
        TimeProvider timeProvider)
    {
        this.context = context;
        this.timeProvider = timeProvider;
        audit = new AuditTrail(context, operationContext, timeProvider);
    }

    public async Task<PagedResult<AdminCourseListItem>> ListCoursesAsync(
        AdminCourseListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        AdminCourseListQuery normalized = query.Normalized();
        IQueryable<Course> courses = context.Courses.AsNoTracking();

        if (normalized.Search is { } search)
        {
            courses = courses.Where(course =>
                EF.Functions.Like(course.Title, $"%{search}%")
                || EF.Functions.Like(course.Slug, $"%{search}%"));
        }

        if (normalized.Status is { } status)
        {
            // An unrecognised status matches nothing rather than being quietly ignored, so a
            // typo cannot look like "everything".
            courses = Enum.TryParse(status, ignoreCase: true, out PublicationStatus parsed)
                ? courses.Where(course => course.Status == parsed)
                : courses.Where(static _ => false);
        }

        int totalCount = await courses.CountAsync(cancellationToken);

        List<AdminCourseListItem> items = await courses
            .OrderBy(course => course.Title)
            .ThenBy(course => course.Id)
            .Skip((normalized.Page - 1) * normalized.PageSize)
            .Take(normalized.PageSize)
            .Select(course => new AdminCourseListItem(
                course.Id,
                course.Slug,
                course.Title,
                course.Status.ToString(),
                course.Level.ToString(),
                course.IncludedInMembership,
                course.PublishedAtUtc,
                course.UpdatedAtUtc,
                course.Sections.Count,
                course.Lessons.Count,
                RowVersionToken.Encode(course.RowVersion)))
            .ToListAsync(cancellationToken);

        int totalPages = normalized.PageSize == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)normalized.PageSize);

        return new PagedResult<AdminCourseListItem>(
            items,
            normalized.Page,
            normalized.PageSize,
            totalCount,
            totalPages);
    }

    public async Task<AdminCourseDetail?> GetCourseAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        Course? course = await LoadGraphAsync(courseId, tracked: false, cancellationToken);

        return course is null ? null : Project(course);
    }

    public async Task<OperationResult<AdminCourseDetail>> CreateCourseAsync(
        CreateCourseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = new ValidationBuilder();
        ValidateCourseFields(
            validation,
            request.Slug,
            request.Title,
            request.Summary,
            request.Description,
            request.Level,
            imageAltText: null);

        if (validation.HasErrors)
        {
            return Fail(validation.ToResult());
        }

        string slug = request.Slug.Trim();

        if (await context.Courses.AnyAsync(course => course.Slug == slug, cancellationToken))
        {
            return Fail(OperationResult.Invalid(
                ErrorCodes.DuplicateValue,
                "slug",
                "Another course already uses this slug."));
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var created = new Course
        {
            Id = Guid.CreateVersion7(),
            Slug = slug,
            Title = request.Title.Trim(),
            Summary = request.Summary.Trim(),
            Description = request.Description.Trim(),
            Level = Enum.Parse<CourseLevel>(request.Level, ignoreCase: true),
            IncludedInMembership = request.IncludedInMembership,
            Status = PublicationStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.Courses.Add(created);
        audit.Append(
            "Catalog.Course.Created",
            nameof(Course),
            created.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["slug"] = created.Slug,
                ["status"] = created.Status.ToString(),
            });

        return await SaveAndReloadAsync(created.Id, cancellationToken);
    }

    public async Task<OperationResult<AdminCourseDetail>> UpdateCourseAsync(
        Guid courseId,
        UpdateCourseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Course? course = await LoadGraphAsync(courseId, tracked: true, cancellationToken);

        if (course is null)
        {
            return Fail(OperationResult.NotFound());
        }

        var validation = new ValidationBuilder();
        ValidateCourseFields(
            validation,
            request.Slug,
            request.Title,
            request.Summary,
            request.Description,
            request.Level,
            request.ImageAltText);

        if (validation.HasErrors)
        {
            return Fail(validation.ToResult());
        }

        string slug = request.Slug.Trim();

        // The slug is part of every public URL and of the identity of purchased content, so it
        // is fixed from the moment the course is first published.
        if (course.PublishedAtUtc is not null
            && !string.Equals(slug, course.Slug, StringComparison.Ordinal))
        {
            return Fail(OperationResult.Invalid(
                ErrorCodes.SlugLocked,
                "slug",
                "The slug cannot change after a course has been published."));
        }

        if (await context.Courses.AnyAsync(
                other => other.Slug == slug && other.Id != courseId,
                cancellationToken))
        {
            return Fail(OperationResult.Invalid(
                ErrorCodes.DuplicateValue,
                "slug",
                "Another course already uses this slug."));
        }

        if (!ApplyRowVersion(course, request.RowVersion))
        {
            return Fail(InvalidRowVersion());
        }

        course.Slug = slug;
        course.Title = request.Title.Trim();
        course.Summary = request.Summary.Trim();
        course.Description = request.Description.Trim();
        course.Level = Enum.Parse<CourseLevel>(request.Level, ignoreCase: true);
        course.IncludedInMembership = request.IncludedInMembership;
        course.ImageAltText = Blank(request.ImageAltText);
        course.UpdatedAtUtc = timeProvider.GetUtcNow();

        audit.Append(
            "Catalog.Course.Updated",
            nameof(Course),
            course.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["slug"] = course.Slug,
                ["fields"] = "slug,title,summary,description,level,includedInMembership,imageAltText",
            });

        return await SaveAndReloadAsync(courseId, cancellationToken);
    }

    public async Task<OperationResult<AdminCourseDetail>> ChangeCourseStatusAsync(
        Guid courseId,
        string targetStatus,
        StatusChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Course? course = await LoadGraphAsync(courseId, tracked: true, cancellationToken);

        if (course is null)
        {
            return Fail(OperationResult.NotFound());
        }

        OperationResult? refusal = ValidateStatusChange(course.Status, targetStatus, request, out PublicationStatus target);

        if (refusal is not null)
        {
            return Fail(refusal);
        }

        if (target == PublicationStatus.Published)
        {
            OperationResult? prerequisite = CoursePublishPrerequisite(course);

            if (prerequisite is not null)
            {
                return Fail(prerequisite);
            }
        }

        if (!ApplyRowVersion(course, request.RowVersion))
        {
            return Fail(InvalidRowVersion());
        }

        PublicationStatus previous = course.Status;
        DateTimeOffset now = timeProvider.GetUtcNow();

        course.Status = target;
        course.UpdatedAtUtc = now;

        // First publication is the one that is recorded. Re-publishing after a withdrawal does
        // not reset the date, so "published since" stays truthful.
        if (target == PublicationStatus.Published)
        {
            course.PublishedAtUtc ??= now;
        }

        AppendStatusAudit("Catalog.Course.StatusChanged", nameof(Course), course.Id, previous, target, request.Reason);

        return await SaveAndReloadAsync(courseId, cancellationToken);
    }

    public async Task<OperationResult<AdminCourseDetail>> SetCourseTagsAsync(
        Guid courseId,
        IReadOnlyList<Guid> tagIds,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tagIds);

        Course? course = await LoadGraphAsync(courseId, tracked: true, cancellationToken);

        if (course is null)
        {
            return Fail(OperationResult.NotFound());
        }

        Guid[] distinct = tagIds.Distinct().ToArray();
        int existing = await context.Tags.CountAsync(tag => distinct.Contains(tag.Id), cancellationToken);

        if (existing != distinct.Length)
        {
            return Fail(OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "tagIds",
                "One or more of the selected tags no longer exists."));
        }

        if (!ApplyRowVersion(course, rowVersion))
        {
            return Fail(InvalidRowVersion());
        }

        context.CourseTags.RemoveRange(
            course.CourseTags.Where(link => !distinct.Contains(link.TagId)));

        foreach (Guid tagId in distinct.Where(
            id => !course.CourseTags.Any(link => link.TagId == id)))
        {
            context.CourseTags.Add(new CourseTag { CourseId = courseId, TagId = tagId });
        }

        // Touching the course guarantees an UPDATE, which is what makes the caller's row
        // version actually load-bearing rather than decorative.
        course.UpdatedAtUtc = timeProvider.GetUtcNow();

        audit.Append(
            "Catalog.Course.TagsChanged",
            nameof(Course),
            course.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tagCount"] = distinct.Length.ToString(CultureInfo.InvariantCulture),
            });

        return await SaveAndReloadAsync(courseId, cancellationToken);
    }

    public async Task<OperationResult<AdminCourseDetail>> CreateSectionAsync(
        Guid courseId,
        CreateSectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Course? course = await LoadGraphAsync(courseId, tracked: true, cancellationToken);

        if (course is null)
        {
            return Fail(OperationResult.NotFound());
        }

        var validation = new ValidationBuilder()
            .Required("title", request.Title, 200, "Title")
            .Optional("description", request.Description, 1000, "Description");

        if (validation.HasErrors)
        {
            return Fail(validation.ToResult());
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var section = new CourseSection
        {
            Id = Guid.CreateVersion7(),
            CourseId = courseId,
            Title = request.Title.Trim(),
            Description = Blank(request.Description),
            SortOrder = NextSortOrder(course.Sections.Select(item => item.SortOrder)),
            Status = PublicationStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.CourseSections.Add(section);
        audit.Append(
            "Catalog.Section.Created",
            nameof(CourseSection),
            section.Id,
            metadata: CourseMetadata(courseId));

        return await SaveAndReloadAsync(courseId, cancellationToken);
    }

    public async Task<OperationResult<AdminCourseDetail>> UpdateSectionAsync(
        Guid courseId,
        Guid sectionId,
        UpdateSectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Course? course = await LoadGraphAsync(courseId, tracked: true, cancellationToken);
        CourseSection? section = course?.Sections.FirstOrDefault(item => item.Id == sectionId);

        if (section is null)
        {
            return Fail(OperationResult.NotFound());
        }

        var validation = new ValidationBuilder()
            .Required("title", request.Title, 200, "Title")
            .Optional("description", request.Description, 1000, "Description");

        if (validation.HasErrors)
        {
            return Fail(validation.ToResult());
        }

        if (!ApplyRowVersion(section, request.RowVersion))
        {
            return Fail(InvalidRowVersion());
        }

        section.Title = request.Title.Trim();
        section.Description = Blank(request.Description);
        section.UpdatedAtUtc = timeProvider.GetUtcNow();

        audit.Append(
            "Catalog.Section.Updated",
            nameof(CourseSection),
            section.Id,
            metadata: CourseMetadata(courseId));

        return await SaveAndReloadAsync(courseId, cancellationToken);
    }

    public async Task<OperationResult<AdminCourseDetail>> ChangeSectionStatusAsync(
        Guid courseId,
        Guid sectionId,
        string targetStatus,
        StatusChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Course? course = await LoadGraphAsync(courseId, tracked: true, cancellationToken);
        CourseSection? section = course?.Sections.FirstOrDefault(item => item.Id == sectionId);

        if (section is null)
        {
            return Fail(OperationResult.NotFound());
        }

        OperationResult? refusal = ValidateStatusChange(
            section.Status, targetStatus, request, out PublicationStatus target);

        if (refusal is not null)
        {
            return Fail(refusal);
        }

        if (!ApplyRowVersion(section, request.RowVersion))
        {
            return Fail(InvalidRowVersion());
        }

        PublicationStatus previous = section.Status;
        section.Status = target;
        section.UpdatedAtUtc = timeProvider.GetUtcNow();

        AppendStatusAudit(
            "Catalog.Section.StatusChanged", nameof(CourseSection), section.Id, previous, target, request.Reason);

        return await SaveAndReloadAsync(courseId, cancellationToken);
    }

    public async Task<OperationResult<AdminCourseDetail>> ReorderSectionsAsync(
        Guid courseId,
        ReorderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Course? course = await LoadGraphAsync(courseId, tracked: true, cancellationToken);

        if (course is null)
        {
            return Fail(OperationResult.NotFound());
        }

        return await ReorderAsync(
            courseId,
            course.Sections,
            request,
            static section => section.Id,
            static section => section.Status,
            (section, order, now) =>
            {
                section.SortOrder = order;
                section.UpdatedAtUtc = now;
            },
            "Catalog.Section.Reordered",
            nameof(CourseSection),
            cancellationToken);
    }

    public async Task<OperationResult<AdminCourseDetail>> CreateLessonAsync(
        Guid courseId,
        Guid sectionId,
        CreateLessonRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Course? course = await LoadGraphAsync(courseId, tracked: true, cancellationToken);
        CourseSection? section = course?.Sections.FirstOrDefault(item => item.Id == sectionId);

        if (course is null || section is null)
        {
            return Fail(OperationResult.NotFound());
        }

        var validation = new ValidationBuilder();
        ValidateLessonFields(
            validation,
            request.Slug,
            request.Title,
            request.Summary,
            request.LessonType,
            request.EstimatedDurationSeconds);

        if (validation.HasErrors)
        {
            return Fail(validation.ToResult());
        }

        string slug = request.Slug.Trim();

        if (course.Lessons.Any(lesson => string.Equals(lesson.Slug, slug, StringComparison.OrdinalIgnoreCase)))
        {
            return Fail(OperationResult.Invalid(
                ErrorCodes.DuplicateValue,
                "slug",
                "Another lesson in this course already uses this slug."));
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var lesson = new Lesson
        {
            Id = Guid.CreateVersion7(),
            CourseId = courseId,
            CourseSectionId = sectionId,
            Slug = slug,
            Title = request.Title.Trim(),
            Summary = Blank(request.Summary),
            LessonType = Enum.Parse<LessonType>(request.LessonType, ignoreCase: true),
            BodyMarkdown = Blank(request.BodyMarkdown),
            SortOrder = NextSortOrder(section.Lessons.Select(item => item.SortOrder)),
            IsPreview = request.IsPreview,
            Status = PublicationStatus.Draft,
            EstimatedDurationSeconds = request.EstimatedDurationSeconds,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.Lessons.Add(lesson);
        audit.Append(
            "Catalog.Lesson.Created",
            nameof(Lesson),
            lesson.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["courseId"] = courseId.ToString("D"),
                ["sectionId"] = sectionId.ToString("D"),
                ["lessonType"] = lesson.LessonType.ToString(),
            });

        return await SaveAndReloadAsync(courseId, cancellationToken);
    }

    public async Task<OperationResult<AdminCourseDetail>> UpdateLessonAsync(
        Guid courseId,
        Guid lessonId,
        UpdateLessonRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Course? course = await LoadGraphAsync(courseId, tracked: true, cancellationToken);
        Lesson? lesson = course?.Lessons.FirstOrDefault(item => item.Id == lessonId);

        if (course is null || lesson is null)
        {
            return Fail(OperationResult.NotFound());
        }

        var validation = new ValidationBuilder();
        ValidateLessonFields(
            validation,
            request.Slug,
            request.Title,
            request.Summary,
            request.LessonType,
            request.EstimatedDurationSeconds);

        if (validation.HasErrors)
        {
            return Fail(validation.ToResult());
        }

        string slug = request.Slug.Trim();

        // A published lesson's slug is a live public preview URL. Renaming it is allowed, but
        // only after the lesson has been taken back to Draft.
        if (lesson.Status == PublicationStatus.Published
            && !string.Equals(slug, lesson.Slug, StringComparison.Ordinal))
        {
            return Fail(OperationResult.Invalid(
                ErrorCodes.SlugLocked,
                "slug",
                "Move the lesson back to Draft before changing its slug."));
        }

        if (course.Lessons.Any(other =>
                other.Id != lessonId
                && string.Equals(other.Slug, slug, StringComparison.OrdinalIgnoreCase)))
        {
            return Fail(OperationResult.Invalid(
                ErrorCodes.DuplicateValue,
                "slug",
                "Another lesson in this course already uses this slug."));
        }

        if (!ApplyRowVersion(lesson, request.RowVersion))
        {
            return Fail(InvalidRowVersion());
        }

        lesson.Slug = slug;
        lesson.Title = request.Title.Trim();
        lesson.Summary = Blank(request.Summary);
        lesson.LessonType = Enum.Parse<LessonType>(request.LessonType, ignoreCase: true);
        lesson.BodyMarkdown = Blank(request.BodyMarkdown);
        lesson.IsPreview = request.IsPreview;
        lesson.EstimatedDurationSeconds = request.EstimatedDurationSeconds;
        lesson.UpdatedAtUtc = timeProvider.GetUtcNow();

        audit.Append(
            "Catalog.Lesson.Updated",
            nameof(Lesson),
            lesson.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["courseId"] = courseId.ToString("D"),
                ["fields"] = "slug,title,summary,lessonType,bodyMarkdown,isPreview,estimatedDurationSeconds",
            });

        return await SaveAndReloadAsync(courseId, cancellationToken);
    }

    public async Task<OperationResult<AdminCourseDetail>> ChangeLessonStatusAsync(
        Guid courseId,
        Guid lessonId,
        string targetStatus,
        StatusChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Course? course = await LoadGraphAsync(courseId, tracked: true, cancellationToken);
        Lesson? lesson = course?.Lessons.FirstOrDefault(item => item.Id == lessonId);

        if (lesson is null)
        {
            return Fail(OperationResult.NotFound());
        }

        OperationResult? refusal = ValidateStatusChange(
            lesson.Status, targetStatus, request, out PublicationStatus target);

        if (refusal is not null)
        {
            return Fail(refusal);
        }

        if (target == PublicationStatus.Published)
        {
            OperationResult? prerequisite = LessonPublishPrerequisite(lesson);

            if (prerequisite is not null)
            {
                return Fail(prerequisite);
            }
        }

        if (!ApplyRowVersion(lesson, request.RowVersion))
        {
            return Fail(InvalidRowVersion());
        }

        PublicationStatus previous = lesson.Status;
        lesson.Status = target;
        lesson.UpdatedAtUtc = timeProvider.GetUtcNow();

        AppendStatusAudit(
            "Catalog.Lesson.StatusChanged", nameof(Lesson), lesson.Id, previous, target, request.Reason);

        return await SaveAndReloadAsync(courseId, cancellationToken);
    }

    public async Task<OperationResult<AdminCourseDetail>> ReorderLessonsAsync(
        Guid courseId,
        Guid sectionId,
        ReorderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Course? course = await LoadGraphAsync(courseId, tracked: true, cancellationToken);
        CourseSection? section = course?.Sections.FirstOrDefault(item => item.Id == sectionId);

        if (section is null)
        {
            return Fail(OperationResult.NotFound());
        }

        return await ReorderAsync(
            courseId,
            section.Lessons,
            request,
            static lesson => lesson.Id,
            static lesson => lesson.Status,
            (lesson, order, now) =>
            {
                lesson.SortOrder = order;
                lesson.UpdatedAtUtc = now;
            },
            "Catalog.Lesson.Reordered",
            nameof(Lesson),
            cancellationToken);
    }

    public async Task<IReadOnlyList<AdminTag>> ListTagsAsync(CancellationToken cancellationToken = default) =>
        await context.Tags
            .AsNoTracking()
            .OrderBy(tag => tag.Name)
            .ThenBy(tag => tag.Id)
            .Take(MaxTagListSize)
            .Select(tag => new AdminTag(tag.Id, tag.Name, tag.NormalizedName))
            .ToListAsync(cancellationToken);

    public async Task<OperationResult<AdminTag>> CreateTagAsync(
        CreateTagRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = new ValidationBuilder().Required("name", request.Name, 64, "Name");

        if (validation.HasErrors)
        {
            return validation.ToResult().ToFailure<AdminTag>();
        }

        string name = request.Name.Trim();
        string normalized = name.ToUpperInvariant();

        if (await context.Tags.AnyAsync(tag => tag.NormalizedName == normalized, cancellationToken))
        {
            return OperationResult.Invalid(
                ErrorCodes.DuplicateValue,
                "name",
                "A tag with this name already exists.").ToFailure<AdminTag>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var tag = new Tag
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            NormalizedName = normalized,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.Tags.Add(tag);
        audit.Append("Catalog.Tag.Created", nameof(Tag), tag.Id);

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.FromValue(new AdminTag(tag.Id, tag.Name, tag.NormalizedName));
    }

    /// <summary>
    /// Two-pass renumber inside one transaction. Everything is renumbered contiguously —
    /// including archived siblings, which the payload deliberately omits — so the final state
    /// has no gaps and no position is shared.
    /// </summary>
    private async Task<OperationResult<AdminCourseDetail>> ReorderAsync<TEntity>(
        Guid courseId,
        ICollection<TEntity> siblings,
        ReorderRequest request,
        Func<TEntity, Guid> idOf,
        Func<TEntity, PublicationStatus> statusOf,
        Action<TEntity, int, DateTimeOffset> assign,
        string action,
        string targetType,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        List<TEntity> movable = siblings
            .Where(item => statusOf(item) != PublicationStatus.Archived)
            .ToList();

        if (request.Items is null || request.Items.Count != movable.Count)
        {
            return Fail(ReorderMismatch());
        }

        var ordered = new List<TEntity>(movable.Count);

        foreach (ReorderItem item in request.Items)
        {
            TEntity? match = movable.FirstOrDefault(candidate => idOf(candidate) == item.Id);

            if (match is null || ordered.Contains(match))
            {
                return Fail(ReorderMismatch());
            }

            if (!ApplyRowVersion(match, item.RowVersion))
            {
                return Fail(InvalidRowVersion());
            }

            ordered.Add(match);
        }

        // Archived siblings keep their relative order but move to the end, so the visible
        // items own positions 0..n-1 and nothing collides.
        ordered.AddRange(siblings
            .Where(item => statusOf(item) == PublicationStatus.Archived)
            .OrderBy(item => idOf(item)));

        DateTimeOffset now = timeProvider.GetUtcNow();

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        for (int index = 0; index < ordered.Count; index++)
        {
            assign(ordered[index], ReorderParkingOffset + index, now);
        }

        OperationResult parked = await SaveAsync(cancellationToken);

        if (!parked.Succeeded)
        {
            // Disposing the transaction without a commit rolls the parked positions back, so a
            // lost race leaves the original order untouched rather than half-renumbered.
            return Fail(parked);
        }

        for (int index = 0; index < ordered.Count; index++)
        {
            assign(ordered[index], index, now);
        }

        audit.Append(
            action,
            targetType,
            courseId,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["courseId"] = courseId.ToString("D"),
                ["itemCount"] = ordered.Count.ToString(CultureInfo.InvariantCulture),
            });

        OperationResult settled = await SaveAsync(cancellationToken);

        if (!settled.Succeeded)
        {
            return Fail(settled);
        }

        await transaction.CommitAsync(cancellationToken);

        return await ReloadAsync(courseId, cancellationToken);
    }

    private static OperationResult ReorderMismatch() => OperationResult.Invalid(
        ErrorCodes.ReorderMismatch,
        "items",
        "The order must list every visible item exactly once. Reload and try again.");

    private static OperationResult InvalidRowVersion() => OperationResult.Invalid(
        ErrorCodes.InvalidRowVersion,
        "rowVersion",
        "The supplied version token is not valid. Reload the record and try again.");

    private static OperationResult<AdminCourseDetail> Fail(OperationResult outcome) =>
        outcome.ToFailure<AdminCourseDetail>();

    private static int NextSortOrder(IEnumerable<int> existing)
    {
        int[] orders = existing.ToArray();

        return orders.Length == 0 ? 0 : orders.Max() + 1;
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, string> CourseMetadata(Guid courseId) =>
        new(StringComparer.Ordinal) { ["courseId"] = courseId.ToString("D") };

    private void AppendStatusAudit(
        string action,
        string targetType,
        Guid targetId,
        PublicationStatus previous,
        PublicationStatus target,
        string reason) =>
        audit.Append(
            action,
            targetType,
            targetId,
            reason,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["previousStatus"] = previous.ToString(),
                ["status"] = target.ToString(),
            });

    private static OperationResult? ValidateStatusChange(
        PublicationStatus current,
        string targetStatus,
        StatusChangeRequest request,
        out PublicationStatus target)
    {
        target = PublicationStatus.Draft;

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "reason",
                "A reason is required for every status change.");
        }

        if (request.Reason.Trim().Length > 512)
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "reason",
                "Reason must be 512 characters or fewer.");
        }

        if (!Enum.TryParse(targetStatus, ignoreCase: true, out target))
        {
            return OperationResult.Invalid(
                ErrorCodes.InvalidTransition,
                "status",
                "Unknown status.");
        }

        return PublicationStatusGraph.CanTransition(current, target)
            ? null
            : OperationResult.Invalid(
                ErrorCodes.InvalidTransition,
                "status",
                $"A {current} record cannot move to {target}.");
    }

    private static OperationResult? CoursePublishPrerequisite(Course course)
    {
        bool hasPublishableOutline = course.Sections.Any(section =>
            section.Status == PublicationStatus.Published
            && section.Lessons.Any(lesson => lesson.Status == PublicationStatus.Published));

        if (!hasPublishableOutline)
        {
            return OperationResult.Invalid(
                ErrorCodes.PublishPrerequisite,
                "status",
                "Publish at least one section containing a published lesson first.");
        }

        return string.IsNullOrWhiteSpace(course.Summary) || string.IsNullOrWhiteSpace(course.Description)
            ? OperationResult.Invalid(
                ErrorCodes.PublishPrerequisite,
                "status",
                "Add a summary and description before publishing.")
            : null;
    }

    private static OperationResult? LessonPublishPrerequisite(Lesson lesson) => lesson.LessonType switch
    {
        LessonType.Article when string.IsNullOrWhiteSpace(lesson.BodyMarkdown) =>
            OperationResult.Invalid(
                ErrorCodes.PublishPrerequisite,
                "status",
                "An article lesson needs body content before it can be published."),
        LessonType.Video when lesson.Video?.Status != LessonVideoStatus.Ready =>
            OperationResult.Invalid(
                ErrorCodes.PublishPrerequisite,
                "status",
                "A video lesson needs a ready video before it can be published."),
        _ => null,
    };

    private static void ValidateCourseFields(
        ValidationBuilder validation,
        string? slug,
        string? title,
        string? summary,
        string? description,
        string? level,
        string? imageAltText)
    {
        validation
            .When(!CatalogSlug.IsValid(slug?.Trim()), "slug", CatalogSlug.Requirement)
            .Required("title", title, 200, "Title")
            .Required("summary", summary, 512, "Summary")
            .Required("description", description, 4000, "Description")
            .Optional("imageAltText", imageAltText, 256, "Image alt text")
            .When(
                !Enum.TryParse(level, ignoreCase: true, out CourseLevel _),
                "level",
                "Choose a valid level.");
    }

    private static void ValidateLessonFields(
        ValidationBuilder validation,
        string? slug,
        string? title,
        string? summary,
        string? lessonType,
        int? estimatedDurationSeconds)
    {
        validation
            .When(!CatalogSlug.IsValid(slug?.Trim()), "slug", CatalogSlug.Requirement)
            .Required("title", title, 200, "Title")
            .Optional("summary", summary, 512, "Summary")
            .When(
                !Enum.TryParse(lessonType, ignoreCase: true, out LessonType _),
                "lessonType",
                "Choose a valid lesson type.")
            .When(
                estimatedDurationSeconds is < 0,
                "estimatedDurationSeconds",
                "Duration cannot be negative.");
    }

    private bool ApplyRowVersion<TEntity>(TEntity entity, string? token)
        where TEntity : class
    {
        if (!RowVersionToken.TryDecode(token, out byte[] bytes))
        {
            return false;
        }

        context.Entry(entity).Property(nameof(Course.RowVersion)).OriginalValue = bytes;
        return true;
    }

    private async Task<Course?> LoadGraphAsync(
        Guid courseId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        IQueryable<Course> query = context.Courses
            .Include(course => course.Sections)
                .ThenInclude(section => section.Lessons)
                    .ThenInclude(lesson => lesson.Video)
            .Include(course => course.CourseTags)
                .ThenInclude(link => link.Tag);

        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(course => course.Id == courseId, cancellationToken);
    }

    private async Task<OperationResult> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return OperationResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            // The caller's row version lost. Detaching keeps the failed attempt out of any
            // later use of this scoped context.
            context.ChangeTracker.Clear();
            return OperationResult.ConcurrencyConflict();
        }
    }

    private async Task<OperationResult<AdminCourseDetail>> SaveAndReloadAsync(
        Guid courseId,
        CancellationToken cancellationToken)
    {
        OperationResult saved = await SaveAsync(cancellationToken);

        return saved.Succeeded
            ? await ReloadAsync(courseId, cancellationToken)
            : Fail(saved);
    }

    private async Task<OperationResult<AdminCourseDetail>> ReloadAsync(
        Guid courseId,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        Course? reloaded = await LoadGraphAsync(courseId, tracked: false, cancellationToken);

        return reloaded is null
            ? Fail(OperationResult.NotFound())
            : OperationResult.FromValue(Project(reloaded));
    }

    private static AdminCourseDetail Project(Course course) => new(
        course.Id,
        course.Slug,
        course.Title,
        course.Summary,
        course.Description,
        course.Level.ToString(),
        course.Status.ToString(),
        course.IncludedInMembership,
        course.ImageAltText,
        course.PublishedAtUtc,
        course.CreatedAtUtc,
        course.UpdatedAtUtc,
        course.PublishedAtUtc is not null,
        course.Sections
            .OrderBy(section => section.SortOrder)
            .Select(section => new AdminSection(
                section.Id,
                section.Title,
                section.Description,
                section.SortOrder,
                section.Status.ToString(),
                section.Lessons
                    .OrderBy(lesson => lesson.SortOrder)
                    .Select(lesson => new AdminLesson(
                        lesson.Id,
                        lesson.Slug,
                        lesson.Title,
                        lesson.Summary,
                        lesson.LessonType.ToString(),
                        lesson.BodyMarkdown,
                        lesson.SortOrder,
                        lesson.IsPreview,
                        lesson.Status.ToString(),
                        lesson.EstimatedDurationSeconds,
                        lesson.Video?.Status.ToString(),
                        RowVersionToken.Encode(lesson.RowVersion)))
                    .ToArray(),
                RowVersionToken.Encode(section.RowVersion)))
            .ToArray(),
        course.CourseTags
            .Where(link => link.Tag is not null)
            .Select(link => new AdminTag(link.Tag!.Id, link.Tag.Name, link.Tag.NormalizedName))
            .OrderBy(tag => tag.Name, StringComparer.Ordinal)
            .ToArray(),
        RowVersionToken.Encode(course.RowVersion));
}
