using DanielsDojo.Application.Catalog;

namespace DanielsDojo.Api.Catalog;

/// <summary>
/// Anonymous catalog endpoints.
/// </summary>
/// <remarks>
/// Every response is a projection built in SQL from Published rows only. A course that is a
/// draft, archived, or simply absent produces the same bare 404, so the API cannot be used to
/// discover unreleased content.
/// </remarks>
internal static class PublicCatalogEndpoints
{
    /// <summary>Maps the public catalog routes.</summary>
    public static void MapPublicCatalogEndpoints(this RouteGroupBuilder apiV1)
    {
        RouteGroupBuilder catalog = apiV1.MapGroup("/catalog").AllowAnonymous();

        catalog.MapGet("/courses", async (
                IPublicCatalogQueries queries,
                CancellationToken cancellationToken,
                string? search = null,
                string? level = null,
                string? tag = null,
                int page = 1,
                int pageSize = CourseListQuery.DefaultPageSize) =>
            {
                PagedResult<CourseCard> result = await queries.ListCoursesAsync(
                    new CourseListQuery(search, level, tag, page, pageSize),
                    cancellationToken);

                return TypedResults.Ok(result);
            })
            .WithName("ListPublicCourses");

        catalog.MapGet("/courses/{slug}", async (
                string slug,
                IPublicCatalogQueries queries,
                CancellationToken cancellationToken) =>
            {
                CourseDetail? course = await queries.GetCourseAsync(slug, cancellationToken);

                return course is null
                    ? Results.NotFound()
                    : Results.Ok(course);
            })
            .WithName("GetPublicCourse");

        catalog.MapGet("/membership", async (
                IPublicCatalogQueries queries,
                CancellationToken cancellationToken) =>
            {
                PublicPrice? price = await queries.GetMembershipPriceAsync(cancellationToken);

                // 404 while no membership price is live: the pricing page says so honestly.
                return price is null ? Results.NotFound() : Results.Ok(price);
            })
            .WithName("GetMembershipPrice");

        catalog.MapGet("/courses/{courseSlug}/lessons/{lessonSlug}/preview", async (
                string courseSlug,
                string lessonSlug,
                IPublicCatalogQueries queries,
                CancellationToken cancellationToken) =>
            {
                LessonPreview? preview =
                    await queries.GetLessonPreviewAsync(courseSlug, lessonSlug, cancellationToken);

                // A non-preview lesson, an unpublished one, and a missing one are
                // indistinguishable from here.
                return preview is null
                    ? Results.NotFound()
                    : Results.Ok(preview);
            })
            .WithName("GetPublicLessonPreview");
    }
}
