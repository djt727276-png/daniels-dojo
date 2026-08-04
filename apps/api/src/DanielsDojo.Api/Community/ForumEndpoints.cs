using DanielsDojo.Api.Authentication;
using DanielsDojo.Api.Common;
using DanielsDojo.Application.Community;
using DanielsDojo.Application.Identity;

namespace DanielsDojo.Api.Community;

/// <summary>
/// Forum reading and writing for signed-in members.
/// </summary>
/// <remarks>
/// Reading requires a session; writing additionally requires community participation, which
/// the service checks through the single access evaluator. The caller is always taken from the
/// resolved local user, never from a body, so nobody can post as somebody else.
/// </remarks>
internal static class ForumEndpoints
{
    /// <summary>Maps the member-facing community routes.</summary>
    public static void MapForumEndpoints(this RouteGroupBuilder apiV1)
    {
        RouteGroupBuilder community = apiV1
            .MapGroup("/community")
            .RequireAuthorization(AuthenticationRegistration.StudentPolicy);

        community.MapGet("/categories", async (
                IForumService forum,
                CancellationToken cancellationToken) =>
            TypedResults.Ok(await forum.ListCategoriesAsync(cancellationToken)))
            .WithName("ListForumCategories");

        community.MapGet("/threads/recent", async (
                ICurrentUser currentUser,
                IForumService forum,
                CancellationToken cancellationToken,
                int count = 5) =>
            TypedResults.Ok(await forum.ListRecentThreadsAsync(
                currentUser.User!.UserId, count, cancellationToken)))
            .WithName("ListRecentForumThreads");

        community.MapGet("/categories/{categorySlug}/threads", async (
                string categorySlug,
                ICurrentUser currentUser,
                IForumService forum,
                CancellationToken cancellationToken,
                int page = 1,
                int pageSize = 20) =>
            OperationResults.ToResponse(
                await forum.ListThreadsAsync(
                    currentUser.User!.UserId, categorySlug, page, pageSize, cancellationToken)))
            .WithName("ListForumThreads");

        community.MapGet("/threads/{threadId:guid}", async (
                Guid threadId,
                ICurrentUser currentUser,
                IForumService forum,
                CancellationToken cancellationToken,
                int page = 1,
                int pageSize = 20) =>
            OperationResults.ToResponse(
                await forum.GetThreadAsync(
                    currentUser.User!.UserId, threadId, page, pageSize, cancellationToken)))
            .WithName("GetForumThread");

        community.MapPost("/threads", async (
                CreateThreadRequest request,
                ICurrentUser currentUser,
                IForumService forum,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await forum.CreateThreadAsync(currentUser.User!.UserId, request, cancellationToken)))
            .WithName("CreateForumThread")
            .RequireRateLimiting(RateLimitPolicies.CommunityWrite);

        community.MapPost("/threads/{threadId:guid}/posts", async (
                Guid threadId,
                CreatePostRequest request,
                ICurrentUser currentUser,
                IForumService forum,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await forum.CreatePostAsync(
                    currentUser.User!.UserId, threadId, request, cancellationToken)))
            .WithName("CreateForumPost")
            .RequireRateLimiting(RateLimitPolicies.CommunityWrite);

        community.MapPut("/posts/{postId:guid}", async (
                Guid postId,
                UpdatePostRequest request,
                ICurrentUser currentUser,
                IForumService forum,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await forum.UpdatePostAsync(
                    currentUser.User!.UserId, postId, request, cancellationToken)))
            .WithName("UpdateForumPost")
            .RequireRateLimiting(RateLimitPolicies.CommunityWrite);

        community.MapDelete("/posts/{postId:guid}", async (
                Guid postId,
                ICurrentUser currentUser,
                IForumService forum,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await forum.RemoveOwnPostAsync(currentUser.User!.UserId, postId, cancellationToken)))
            .WithName("RemoveOwnForumPost");

        community.MapPut("/posts/{postId:guid}/reaction", async (
                Guid postId,
                ReactionRequest request,
                ICurrentUser currentUser,
                IForumService forum,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await forum.SetReactionAsync(
                    currentUser.User!.UserId, postId, request.Liked, cancellationToken)))
            .WithName("SetForumReaction")
            .RequireRateLimiting(RateLimitPolicies.CommunityReaction);

        community.MapPut("/threads/{threadId:guid}/subscription", async (
                Guid threadId,
                SubscriptionRequest request,
                ICurrentUser currentUser,
                IForumService forum,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await forum.SetSubscriptionAsync(
                    currentUser.User!.UserId, threadId, request.Subscribed, cancellationToken)))
            .WithName("SetForumSubscription");

        community.MapPost("/reports", async (
                CreateReportRequest request,
                ICurrentUser currentUser,
                IForumService forum,
                CancellationToken cancellationToken) =>
            {
                Application.Common.OperationResult result =
                    await forum.ReportAsync(currentUser.User!.UserId, request, cancellationToken);

                return result.Succeeded ? Results.Accepted() : OperationResults.ToProblem(result);
            })
            .WithName("CreateCommunityReport")
            .RequireRateLimiting(RateLimitPolicies.CommunityReport);
    }
}

/// <summary>Sets or clears the caller's like on a post.</summary>
internal sealed record ReactionRequest(bool Liked);

/// <summary>Subscribes or unsubscribes the caller from a thread.</summary>
internal sealed record SubscriptionRequest(bool Subscribed);
