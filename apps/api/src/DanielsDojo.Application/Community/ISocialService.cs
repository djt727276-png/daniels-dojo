using DanielsDojo.Application.Catalog;
using DanielsDojo.Application.Common;

namespace DanielsDojo.Application.Community;

/// <summary>
/// Friendships, blocks, direct messages, and the notification inbox.
/// </summary>
/// <remarks>
/// Members are addressed by handle from the client, never by internal identifier, so a caller
/// cannot enumerate accounts by guessing. A block is honoured in both directions everywhere
/// here: it hides content, silences notifications, and refuses new contact.
/// </remarks>
public interface ISocialService
{
    /// <summary>Searches discoverable profiles by handle.</summary>
    Task<OperationResult<IReadOnlyList<MemberCard>>> SearchMembersAsync(
        Guid viewerUserId,
        string? search,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one member's card, when the viewer is allowed to see it.</summary>
    Task<OperationResult<MemberCard>> GetMemberAsync(
        Guid viewerUserId,
        string handle,
        CancellationToken cancellationToken = default);

    /// <summary>Lists accepted friends.</summary>
    Task<IReadOnlyList<FriendView>> ListFriendsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists pending friend requests in both directions.</summary>
    Task<IReadOnlyList<FriendRequestView>> ListFriendRequestsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a friend request.</summary>
    Task<OperationResult> SendFriendRequestAsync(
        Guid senderUserId,
        SendFriendRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Accepts, declines, or cancels a pending request.</summary>
    Task<OperationResult> RespondToFriendRequestAsync(
        Guid userId,
        Guid requestId,
        string action,
        CancellationToken cancellationToken = default);

    /// <summary>Ends a friendship.</summary>
    Task<OperationResult> RemoveFriendAsync(
        Guid userId,
        Guid otherUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists members this viewer has blocked.</summary>
    Task<IReadOnlyList<BlockView>> ListBlocksAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Blocks a member, ending any friendship and cancelling pending requests.</summary>
    Task<OperationResult> BlockAsync(
        Guid userId,
        CreateBlockRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a block.</summary>
    Task<OperationResult> UnblockAsync(
        Guid userId,
        Guid blockedUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the member's conversations.</summary>
    Task<IReadOnlyList<ConversationSummary>> ListConversationsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Opens or creates the conversation with a member.</summary>
    Task<OperationResult<ConversationDetail>> StartConversationAsync(
        Guid userId,
        StartConversationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a conversation with a page of messages and marks it read.</summary>
    Task<OperationResult<ConversationDetail>> GetConversationAsync(
        Guid userId,
        Guid conversationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a message into an existing conversation.</summary>
    Task<OperationResult<ConversationDetail>> SendMessageAsync(
        Guid userId,
        Guid conversationId,
        SendMessageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Tombstones the caller's own message.</summary>
    Task<OperationResult<ConversationDetail>> DeleteMessageAsync(
        Guid userId,
        Guid messageId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the notification inbox, newest first.</summary>
    Task<PagedResult<NotificationView>> ListNotificationsAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Marks one notification, or all of them, as read.</summary>
    Task<OperationResult> MarkNotificationsReadAsync(
        Guid userId,
        Guid? notificationId,
        CancellationToken cancellationToken = default);
}
