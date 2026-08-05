using DanielsDojo.Api.Authentication;
using DanielsDojo.Api.Common;
using DanielsDojo.Application.Common;
using DanielsDojo.Application.Community;
using DanielsDojo.Application.Identity;

namespace DanielsDojo.Api.Community;

/// <summary>
/// Friendships, blocks, direct messages, and the notification inbox.
/// </summary>
/// <remarks>
/// Other members are addressed by handle, so no route lets a caller sweep internal identifiers.
/// Sending anything is rate limited per local application user, which is the one identifier a
/// caller cannot change or forge.
/// </remarks>
internal static class SocialEndpoints
{
    /// <summary>Maps the social and notification routes.</summary>
    public static void MapSocialEndpoints(this RouteGroupBuilder apiV1)
    {
        RouteGroupBuilder community = apiV1
            .MapGroup("/community")
            .RequireAuthorization(AuthenticationRegistration.StudentPolicy);

        MapPeople(community);
        MapFriends(community);
        MapBlocks(community);
        MapConversations(community);

        community.MapGet("/avatars/{userId:guid}", async (
                Guid userId,
                ICurrentUser currentUser,
                IAvatarService avatars,
                CancellationToken cancellationToken) =>
            {
                AvatarContent? avatar = await avatars.GetAsync(
                    currentUser.User!.UserId, userId, cancellationToken);

                // Absent and hidden-by-block are deliberately the same answer.
                return avatar is null
                    ? Results.NotFound()
                    : Results.File(
                        avatar.Bytes,
                        avatar.ContentType,
                        entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue(
                            avatar.ETag));
            })
            .WithName("GetMemberAvatar");

        RouteGroupBuilder me = apiV1
            .MapGroup("/me")
            .RequireAuthorization(AuthenticationRegistration.StudentPolicy);

        me.MapGet("/notifications", async (
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken,
                int page = 1,
                int pageSize = 30) =>
            TypedResults.Ok(await social.ListNotificationsAsync(
                currentUser.User!.UserId, page, pageSize, cancellationToken)))
            .WithName("ListNotifications");

        me.MapPut("/notifications/read", async (
                MarkReadRequest request,
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken) =>
            Respond(await social.MarkNotificationsReadAsync(
                currentUser.User!.UserId, request.NotificationId, cancellationToken)))
            .WithName("MarkNotificationsRead");
    }

    private static void MapPeople(RouteGroupBuilder community)
    {
        community.MapGet("/people", async (
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken,
                string? search = null) =>
            OperationResults.ToResponse(
                await social.SearchMembersAsync(currentUser.User!.UserId, search, cancellationToken)))
            .WithName("SearchMembers")
            .RequireRateLimiting(RateLimitPolicies.ProfileSearch);

        community.MapGet("/people/{handle}", async (
                string handle,
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await social.GetMemberAsync(currentUser.User!.UserId, handle, cancellationToken)))
            .WithName("GetMember");
    }

    private static void MapFriends(RouteGroupBuilder community)
    {
        community.MapGet("/friends", async (
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken) =>
            TypedResults.Ok(
                await social.ListFriendsAsync(currentUser.User!.UserId, cancellationToken)))
            .WithName("ListFriends");

        community.MapDelete("/friends/{otherUserId:guid}", async (
                Guid otherUserId,
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken) =>
            Respond(await social.RemoveFriendAsync(
                currentUser.User!.UserId, otherUserId, cancellationToken)))
            .WithName("RemoveFriend");

        community.MapGet("/friend-requests", async (
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken) =>
            TypedResults.Ok(
                await social.ListFriendRequestsAsync(currentUser.User!.UserId, cancellationToken)))
            .WithName("ListFriendRequests");

        community.MapPost("/friend-requests", async (
                SendFriendRequest request,
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken) =>
            Respond(await social.SendFriendRequestAsync(
                currentUser.User!.UserId, request, cancellationToken)))
            .WithName("SendFriendRequest")
            .RequireRateLimiting(RateLimitPolicies.CommunityFriendRequest);

        community.MapPost("/friend-requests/{requestId:guid}/{action}", async (
                Guid requestId,
                string action,
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken) =>
            Respond(await social.RespondToFriendRequestAsync(
                currentUser.User!.UserId, requestId, action, cancellationToken)))
            .WithName("RespondToFriendRequest");
    }

    private static void MapBlocks(RouteGroupBuilder community)
    {
        community.MapGet("/blocks", async (
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken) =>
            TypedResults.Ok(await social.ListBlocksAsync(currentUser.User!.UserId, cancellationToken)))
            .WithName("ListBlocks");

        community.MapPost("/blocks", async (
                CreateBlockRequest request,
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken) =>
            Respond(await social.BlockAsync(currentUser.User!.UserId, request, cancellationToken)))
            .WithName("BlockMember");

        community.MapDelete("/blocks/{blockedUserId:guid}", async (
                Guid blockedUserId,
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken) =>
            Respond(await social.UnblockAsync(
                currentUser.User!.UserId, blockedUserId, cancellationToken)))
            .WithName("UnblockMember");
    }

    private static void MapConversations(RouteGroupBuilder community)
    {
        community.MapGet("/conversations", async (
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken) =>
            TypedResults.Ok(
                await social.ListConversationsAsync(currentUser.User!.UserId, cancellationToken)))
            .WithName("ListConversations");

        community.MapPost("/conversations", async (
                StartConversationRequest request,
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(await social.StartConversationAsync(
                currentUser.User!.UserId, request, cancellationToken)))
            .WithName("StartConversation")
            .RequireRateLimiting(RateLimitPolicies.CommunityMessage);

        community.MapGet("/conversations/{conversationId:guid}", async (
                Guid conversationId,
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken,
                int page = 1,
                int pageSize = 30) =>
            OperationResults.ToResponse(await social.GetConversationAsync(
                currentUser.User!.UserId, conversationId, page, pageSize, cancellationToken)))
            .WithName("GetConversation");

        community.MapPost("/conversations/{conversationId:guid}/messages", async (
                Guid conversationId,
                SendMessageRequest request,
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(await social.SendMessageAsync(
                currentUser.User!.UserId, conversationId, request, cancellationToken)))
            .WithName("SendDirectMessage")
            .RequireRateLimiting(RateLimitPolicies.CommunityMessage);

        community.MapDelete("/messages/{messageId:guid}", async (
                Guid messageId,
                ICurrentUser currentUser,
                ISocialService social,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(await social.DeleteMessageAsync(
                currentUser.User!.UserId, messageId, cancellationToken)))
            .WithName("DeleteDirectMessage");
    }

    private static IResult Respond(OperationResult result) =>
        result.Succeeded ? Results.NoContent() : OperationResults.ToProblem(result);
}

/// <summary>Marks one notification read, or every unread one when the identifier is omitted.</summary>
internal sealed record MarkReadRequest(Guid? NotificationId);
