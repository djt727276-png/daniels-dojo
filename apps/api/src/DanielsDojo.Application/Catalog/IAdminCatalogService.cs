using DanielsDojo.Application.Common;

namespace DanielsDojo.Application.Catalog;

/// <summary>
/// Authoring operations over the catalog, for database-backed Admins only.
/// </summary>
/// <remarks>
/// Every mutation returns the complete course detail so the caller always holds current row
/// versions for the course and every section and lesson beneath it. That removes the common
/// failure where a client applies one edit successfully and then fails the next with a stale
/// token it had no way to refresh.
/// </remarks>
public interface IAdminCatalogService
{
    /// <summary>Lists courses in every status.</summary>
    Task<PagedResult<AdminCourseListItem>> ListCoursesAsync(
        AdminCourseListQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Returns full editor detail, or null when the course does not exist.</summary>
    Task<AdminCourseDetail?> GetCourseAsync(Guid courseId, CancellationToken cancellationToken = default);

    /// <summary>Creates a Draft course.</summary>
    Task<OperationResult<AdminCourseDetail>> CreateCourseAsync(
        CreateCourseRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a course's editable metadata.</summary>
    Task<OperationResult<AdminCourseDetail>> UpdateCourseAsync(
        Guid courseId,
        UpdateCourseRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Moves a course through the publication status graph.</summary>
    Task<OperationResult<AdminCourseDetail>> ChangeCourseStatusAsync(
        Guid courseId,
        string targetStatus,
        StatusChangeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the course's tag assignments.</summary>
    Task<OperationResult<AdminCourseDetail>> SetCourseTagsAsync(
        Guid courseId,
        IReadOnlyList<Guid> tagIds,
        string rowVersion,
        CancellationToken cancellationToken = default);

    /// <summary>Appends a Draft section at the end of the course.</summary>
    Task<OperationResult<AdminCourseDetail>> CreateSectionAsync(
        Guid courseId,
        CreateSectionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a section's editable metadata.</summary>
    Task<OperationResult<AdminCourseDetail>> UpdateSectionAsync(
        Guid courseId,
        Guid sectionId,
        UpdateSectionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Moves a section through the publication status graph.</summary>
    Task<OperationResult<AdminCourseDetail>> ChangeSectionStatusAsync(
        Guid courseId,
        Guid sectionId,
        string targetStatus,
        StatusChangeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reorders every non-archived section of a course in one transaction.</summary>
    Task<OperationResult<AdminCourseDetail>> ReorderSectionsAsync(
        Guid courseId,
        ReorderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Appends a Draft lesson at the end of a section.</summary>
    Task<OperationResult<AdminCourseDetail>> CreateLessonAsync(
        Guid courseId,
        Guid sectionId,
        CreateLessonRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a lesson's editable metadata.</summary>
    Task<OperationResult<AdminCourseDetail>> UpdateLessonAsync(
        Guid courseId,
        Guid lessonId,
        UpdateLessonRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Moves a lesson through the publication status graph.</summary>
    Task<OperationResult<AdminCourseDetail>> ChangeLessonStatusAsync(
        Guid courseId,
        Guid lessonId,
        string targetStatus,
        StatusChangeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reorders every non-archived lesson in a section in one transaction.</summary>
    Task<OperationResult<AdminCourseDetail>> ReorderLessonsAsync(
        Guid courseId,
        Guid sectionId,
        ReorderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every catalog tag.</summary>
    Task<IReadOnlyList<AdminTag>> ListTagsAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a tag, refusing a name that normalizes onto an existing one.</summary>
    Task<OperationResult<AdminTag>> CreateTagAsync(
        CreateTagRequest request,
        CancellationToken cancellationToken = default);
}
