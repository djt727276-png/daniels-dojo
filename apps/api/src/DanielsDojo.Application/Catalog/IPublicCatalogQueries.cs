namespace DanielsDojo.Application.Catalog;

/// <summary>
/// Read-only access to the public catalog. A narrow, purpose-built contract for the public
/// slice — not a generic repository.
/// </summary>
/// <remarks>
/// Every implementation must return Published data only. "Not published" and "does not exist"
/// deliberately produce the same null result so a caller cannot use the difference to
/// enumerate unreleased content.
/// </remarks>
public interface IPublicCatalogQueries
{
    /// <summary>Lists published courses matching the query.</summary>
    Task<PagedResult<CourseCard>> ListCoursesAsync(
        CourseListQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a published course, or null.</summary>
    Task<CourseDetail?> GetCourseAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a preview lesson's body, or null unless the course, its section, and the lesson
    /// are all Published and the lesson is a preview Article.
    /// </summary>
    Task<LessonPreview?> GetLessonPreviewAsync(
        string courseSlug,
        string lessonSlug,
        CancellationToken cancellationToken = default);
}
