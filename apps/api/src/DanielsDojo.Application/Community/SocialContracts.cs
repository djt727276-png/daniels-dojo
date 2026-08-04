using DanielsDojo.Application.Catalog;

namespace DanielsDojo.Application.Community;

/// <summary>
/// Another member as this viewer is allowed to see them.
/// </summary>
/// <remarks>
/// Carries a handle and a bio and nothing else. No email, no display name from the identity
/// provider, and no internal identifier beyond the one needed to act on the relationship.
/// </remarks>
public sealed record MemberCard(
    Guid UserId,
    string Handle,
    string? Bio,
    bool IsFriend,
    bool RequestPending,
    bool CanReceiveFriendRequests,
    bool CanReceiveMessages);

/// <summary>A pending friend request from this member's point of view.</summary>
public sealed record FriendRequestView(
    Guid Id,
    Guid OtherUserId,
    string OtherHandle,
    bool Incoming,
    DateTimeOffset RequestedAtUtc,
    string RowVersion);

/// <summary>An accepted friendship.</summary>
public sealed record FriendView(Guid UserId, string Handle, DateTimeOffset AcceptedAtUtc);

/// <summary>A member this viewer has blocked.</summary>
public sealed record BlockView(Guid UserId, string Handle, string ReasonCategory, DateTimeOffset CreatedAtUtc);

/// <summary>A direct conversation summary.</summary>
public sealed record ConversationSummary(
    Guid Id,
    Guid OtherUserId,
    string OtherHandle,
    bool OtherHidden,
    DateTimeOffset? LastMessageAtUtc,
    int UnreadCount);

/// <summary>
/// One direct message.
/// </summary>
/// <remarks>
/// <see cref="Body"/> is empty for a deleted message. The row survives so the conversation
/// still reads in order, but the text is gone from the database, not merely hidden.
/// </remarks>
public sealed record DirectMessageView(
    Guid Id,
    bool IsOwn,
    string Body,
    string Status,
    bool Withheld,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? EditedAtUtc,
    string RowVersion);

/// <summary>A conversation with a page of its messages.</summary>
public sealed record ConversationDetail(
    Guid Id,
    Guid OtherUserId,
    string OtherHandle,
    bool CanSend,
    string? CannotSendReason,
    PagedResult<DirectMessageView> Messages);

/// <summary>An entry in the notification inbox. Carries a pointer, never content.</summary>
public sealed record NotificationView(
    Guid Id,
    string Kind,
    string? ActorHandle,
    string TargetType,
    Guid TargetId,
    DateTimeOffset CreatedAtUtc,
    bool Read);

/// <summary>Sends a friend request to a member identified by handle.</summary>
public sealed record SendFriendRequest(string Handle);

/// <summary>Blocks a member, optionally recording why.</summary>
public sealed record CreateBlockRequest(string Handle, string? ReasonCategory);

/// <summary>Starts or continues a conversation with a member identified by handle.</summary>
public sealed record StartConversationRequest(string Handle);

/// <summary>Sends a direct message.</summary>
public sealed record SendMessageRequest(string Body);
