using DanielsDojo.Api.Authentication;
using DanielsDojo.Api.Common;
using DanielsDojo.Application.Catalog;
using DanielsDojo.Application.Common;

namespace DanielsDojo.Api.Catalog;

/// <summary>
/// Catalog authoring endpoints, restricted to database-backed Admins.
/// </summary>
/// <remarks>
/// The whole group carries the Admin policy, so a route added later is protected by default
/// rather than by the author remembering. Status changes are explicit commands
/// (<c>/status/publish</c>) rather than a writable status field, which is what lets the API
/// demand a reason and validate the transition instead of accepting whatever a client sends.
/// </remarks>
internal static class AdminCatalogEndpoints
{
    /// <summary>Maps the Admin catalog routes.</summary>
    public static void MapAdminCatalogEndpoints(this RouteGroupBuilder apiV1)
    {
        RouteGroupBuilder admin = apiV1
            .MapGroup("/admin/catalog")
            .RequireAuthorization(AuthenticationRegistration.AdminPolicy);

        MapCourses(admin);
        MapSections(admin);
        MapLessons(admin);
        MapTags(admin);
    }

    private static void MapCourses(RouteGroupBuilder admin)
    {
        admin.MapGet("/courses", async (
                IAdminCatalogService service,
                CancellationToken cancellationToken,
                string? search = null,
                string? status = null,
                int page = 1,
                int pageSize = AdminCourseListQuery.DefaultPageSize) =>
            TypedResults.Ok(await service.ListCoursesAsync(
                new AdminCourseListQuery(search, status, page, pageSize),
                cancellationToken)))
            .WithName("ListAdminCourses");

        admin.MapGet("/courses/{courseId:guid}", async (
                Guid courseId,
                IAdminCatalogService service,
                CancellationToken cancellationToken) =>
            {
                AdminCourseDetail? course = await service.GetCourseAsync(courseId, cancellationToken);

                return course is null ? Results.NotFound() : Results.Ok(course);
            })
            .WithName("GetAdminCourse");

        admin.MapPost("/courses", async (
                CreateCourseRequest request,
                IAdminCatalogService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToCreated(
                await service.CreateCourseAsync(request, cancellationToken),
                static course => $"/api/v1/admin/catalog/courses/{course.Id}"))
            .WithName("CreateAdminCourse");

        admin.MapPut("/courses/{courseId:guid}", async (
                Guid courseId,
                UpdateCourseRequest request,
                IAdminCatalogService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.UpdateCourseAsync(courseId, request, cancellationToken)))
            .WithName("UpdateAdminCourse");

        admin.MapPost("/courses/{courseId:guid}/status/{targetStatus}", async (
                Guid courseId,
                string targetStatus,
                StatusChangeRequest request,
                IAdminCatalogService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.ChangeCourseStatusAsync(
                    courseId, targetStatus, request, cancellationToken)))
            .WithName("ChangeAdminCourseStatus");

        admin.MapPut("/courses/{courseId:guid}/tags", async (
                Guid courseId,
                SetCourseTagsRequest request,
                IAdminCatalogService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.SetCourseTagsAsync(
                    courseId, request.TagIds, request.RowVersion, cancellationToken)))
            .WithName("SetAdminCourseTags");
    }

    private static void MapSections(RouteGroupBuilder admin)
    {
        admin.MapPost("/courses/{courseId:guid}/sections", async (
                Guid courseId,
                CreateSectionRequest request,
                IAdminCatalogService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.CreateSectionAsync(courseId, request, cancellationToken)))
            .WithName("CreateAdminSection");

        admin.MapPut("/courses/{courseId:guid}/sections/{sectionId:guid}", async (
                Guid courseId,
                Guid sectionId,
                UpdateSectionRequest request,
                IAdminCatalogService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.UpdateSectionAsync(courseId, sectionId, request, cancellationToken)))
            .WithName("UpdateAdminSection");

        admin.MapPost("/courses/{courseId:guid}/sections/{sectionId:guid}/status/{targetStatus}", async (
                Guid courseId,
                Guid sectionId,
                string targetStatus,
                StatusChangeRequest request,
                IAdminCatalogService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.ChangeSectionStatusAsync(
                    courseId, sectionId, targetStatus, request, cancellationToken)))
            .WithName("ChangeAdminSectionStatus");

        admin.MapPost("/courses/{courseId:guid}/sections/order", async (
                Guid courseId,
                ReorderRequest request,
                IAdminCatalogService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.ReorderSectionsAsync(courseId, request, cancellationToken)))
            .WithName("ReorderAdminSections");
    }

    private static void MapLessons(RouteGroupBuilder admin)
    {
        admin.MapPost("/courses/{courseId:guid}/sections/{sectionId:guid}/lessons", async (
                Guid courseId,
                Guid sectionId,
                CreateLessonRequest request,
                IAdminCatalogService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.CreateLessonAsync(courseId, sectionId, request, cancellationToken)))
            .WithName("CreateAdminLesson");

        admin.MapPut("/courses/{courseId:guid}/lessons/{lessonId:guid}", async (
                Guid courseId,
                Guid lessonId,
                UpdateLessonRequest request,
                IAdminCatalogService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.UpdateLessonAsync(courseId, lessonId, request, cancellationToken)))
            .WithName("UpdateAdminLesson");

        admin.MapPost("/courses/{courseId:guid}/lessons/{lessonId:guid}/status/{targetStatus}", async (
                Guid courseId,
                Guid lessonId,
                string targetStatus,
                StatusChangeRequest request,
                IAdminCatalogService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.ChangeLessonStatusAsync(
                    courseId, lessonId, targetStatus, request, cancellationToken)))
            .WithName("ChangeAdminLessonStatus");

        admin.MapPost("/courses/{courseId:guid}/sections/{sectionId:guid}/lessons/order", async (
                Guid courseId,
                Guid sectionId,
                ReorderRequest request,
                IAdminCatalogService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.ReorderLessonsAsync(courseId, sectionId, request, cancellationToken)))
            .WithName("ReorderAdminLessons");
    }

    private static void MapTags(RouteGroupBuilder admin)
    {
        admin.MapGet("/tags", async (
                IAdminCatalogService service,
                CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.ListTagsAsync(cancellationToken)))
            .WithName("ListAdminTags");

        admin.MapPost("/tags", async (
                CreateTagRequest request,
                IAdminCatalogService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(await service.CreateTagAsync(request, cancellationToken)))
            .WithName("CreateAdminTag");
    }
}

/// <summary>Replaces a course's tag assignments.</summary>
/// <param name="TagIds">The complete set of tags the course should carry.</param>
/// <param name="RowVersion">The course row version the caller last read.</param>
internal sealed record SetCourseTagsRequest(IReadOnlyList<Guid> TagIds, string RowVersion);
