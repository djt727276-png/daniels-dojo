using DanielsDojo.Api.Authentication;
using DanielsDojo.Api.Common;
using DanielsDojo.Application.Common;
using DanielsDojo.Application.Community;
using DanielsDojo.Application.Identity;

namespace DanielsDojo.Api.Community;

/// <summary>
/// Moderation, restricted to database-backed Admins.
/// </summary>
/// <remarks>
/// Every route takes a reason and records it. There is deliberately no endpoint that lists or
/// reads arbitrary private conversations: review is always scoped to a specific reported
/// target, which is what keeps moderation from becoming general surveillance.
/// </remarks>
internal static class ModerationEndpoints
{
    /// <summary>Maps the moderation routes.</summary>
    public static void MapModerationEndpoints(this RouteGroupBuilder apiV1)
    {
        RouteGroupBuilder moderation = apiV1
            .MapGroup("/admin/community")
            .RequireAuthorization(AuthenticationRegistration.AdminPolicy);

        moderation.MapGet("/overview", async (
                IModerationService service,
                CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetOverviewAsync(cancellationToken)))
            .WithName("GetAdminOverview");

        moderation.MapGet("/categories", async (
                IModerationService service,
                CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.ListCategoriesAsync(cancellationToken)))
            .WithName("ListAdminForumCategories");

        moderation.MapPost("/courses/{courseId:guid}/announcements", async (
                Guid courseId,
                PostAnnouncementRequest request,
                ICurrentUser currentUser,
                IAnnouncementService announcements,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await announcements.PostAsync(
                    currentUser.User!.UserId, courseId, request, cancellationToken)))
            .WithName("PostCourseAnnouncement");

        moderation.MapPost("/categories", async (
                CreateForumCategoryRequest request,
                IModerationService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(await service.CreateCategoryAsync(request, cancellationToken)))
            .WithName("CreateForumCategory");

        moderation.MapPut("/categories/{categoryId:guid}", async (
                Guid categoryId,
                UpdateForumCategoryRequest request,
                IModerationService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.UpdateCategoryAsync(categoryId, request, cancellationToken)))
            .WithName("UpdateForumCategory");

        moderation.MapPost("/categories/{categoryId:guid}/status/{targetStatus}", async (
                Guid categoryId,
                string targetStatus,
                ModerationDecisionRequest request,
                IModerationService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.SetCategoryStatusAsync(
                    categoryId, targetStatus, request, cancellationToken)))
            .WithName("SetForumCategoryStatus");

        moderation.MapGet("/reports", async (
                IModerationService service,
                CancellationToken cancellationToken,
                string? status = null,
                int page = 1,
                int pageSize = 25) =>
            TypedResults.Ok(await service.ListReportsAsync(status, page, pageSize, cancellationToken)))
            .WithName("ListModerationReports");

        // The single route to a reported private message. It is keyed on an open report,
        // returns that one target, and audits the read.
        moderation.MapGet("/reports/{reportId:guid}/target", async (
                Guid reportId,
                ICurrentUser currentUser,
                IModerationService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.GetReportTargetAsync(
                    currentUser.User!.UserId, reportId, cancellationToken)))
            .WithName("GetModerationReportTarget");

        moderation.MapPost("/reports/{reportId:guid}/status/{targetStatus}", async (
                Guid reportId,
                string targetStatus,
                ModerationDecisionRequest request,
                ICurrentUser currentUser,
                IModerationService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.DecideReportAsync(
                    currentUser.User!.UserId, reportId, targetStatus, request, cancellationToken)))
            .WithName("DecideModerationReport");

        moderation.MapPost("/posts/{postId:guid}/remove", async (
                Guid postId,
                ModerationActionRequest request,
                ICurrentUser currentUser,
                IModerationService service,
                CancellationToken cancellationToken) =>
            Respond(await service.RemovePostAsync(
                currentUser.User!.UserId, postId, request, cancellationToken)))
            .WithName("RemoveForumPostAsModerator");

        moderation.MapPost("/threads/{threadId:guid}/status/{targetStatus}", async (
                Guid threadId,
                string targetStatus,
                ModerationActionRequest request,
                ICurrentUser currentUser,
                IModerationService service,
                CancellationToken cancellationToken) =>
            Respond(await service.SetThreadStatusAsync(
                currentUser.User!.UserId, threadId, targetStatus, request, cancellationToken)))
            .WithName("SetForumThreadStatus");

        moderation.MapPost("/threads/{threadId:guid}/pin", async (
                Guid threadId,
                PinRequest request,
                ICurrentUser currentUser,
                IModerationService service,
                CancellationToken cancellationToken) =>
            Respond(await service.SetThreadPinnedAsync(
                currentUser.User!.UserId,
                threadId,
                request.Pinned,
                new ModerationActionRequest(request.Reason),
                cancellationToken)))
            .WithName("SetForumThreadPin");

        moderation.MapPost("/profiles/{targetUserId:guid}/status/{targetStatus}", async (
                Guid targetUserId,
                string targetStatus,
                ModerationActionRequest request,
                ICurrentUser currentUser,
                IModerationService service,
                CancellationToken cancellationToken) =>
            Respond(await service.SetProfileStatusAsync(
                currentUser.User!.UserId, targetUserId, targetStatus, request, cancellationToken)))
            .WithName("SetCommunityProfileStatus");
    }

    private static IResult Respond(OperationResult result) =>
        result.Succeeded ? Results.NoContent() : OperationResults.ToProblem(result);
}

/// <summary>Pins or unpins a thread, with the reason the audit trail records.</summary>
internal sealed record PinRequest(bool Pinned, string Reason);
