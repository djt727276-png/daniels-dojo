namespace DanielsDojo.Domain.Community;

/// <summary>Who may send this member a friend request.</summary>
public enum FriendRequestPolicy
{
    /// <summary>Nobody. The safe default until the member opts in.</summary>
    NoOne,

    /// <summary>Any member who can discover the profile.</summary>
    Everyone,
}

/// <summary>
/// Who may send this member a direct message. Deliberately has no "Everyone" member:
/// unsolicited direct messaging is not offered at launch, so the model cannot express it.
/// </summary>
public enum MessagePolicy
{
    /// <summary>Nobody. The safe default until the member opts in.</summary>
    NoOne,

    /// <summary>Only accepted friends.</summary>
    FriendsOnly,
}

/// <summary>Lifecycle of a community profile.</summary>
public enum CommunityProfileStatus
{
    /// <summary>Participating normally.</summary>
    Active,

    /// <summary>Suspended by a moderator. Content is retained.</summary>
    Suspended,

    /// <summary>Deactivated by the member.</summary>
    Deactivated,
}

/// <summary>Lifecycle of a forum category.</summary>
public enum ForumCategoryStatus
{
    /// <summary>Visible and accepting threads.</summary>
    Active,

    /// <summary>Hidden from ordinary listings; existing threads are retained.</summary>
    Archived,
}

/// <summary>Lifecycle of a forum thread.</summary>
public enum ForumThreadStatus
{
    /// <summary>Accepting replies.</summary>
    Open,

    /// <summary>Readable but closed to ordinary replies.</summary>
    Locked,

    /// <summary>Withdrawn from normal listings.</summary>
    Archived,

    /// <summary>Tombstoned by a moderator. Row retained for audit.</summary>
    Removed,
}

/// <summary>Lifecycle of a forum post.</summary>
public enum ForumPostStatus
{
    /// <summary>Visible.</summary>
    Published,

    /// <summary>Visible and edited by its author.</summary>
    Edited,

    /// <summary>Tombstoned. Body is cleared; the row is retained for thread continuity.</summary>
    Removed,
}

/// <summary>Reaction kinds. Only <see cref="Like"/> is offered at launch.</summary>
public enum ReactionType
{
    /// <summary>A simple positive acknowledgement.</summary>
    Like,
}

/// <summary>Lifecycle of a friend request.</summary>
public enum FriendRequestStatus
{
    /// <summary>Awaiting a response.</summary>
    Pending,

    /// <summary>Accepted; a friendship now exists.</summary>
    Accepted,

    /// <summary>Declined by the recipient.</summary>
    Declined,

    /// <summary>Withdrawn by the sender.</summary>
    Cancelled,
}

/// <summary>Lifecycle of a direct message.</summary>
public enum DirectMessageStatus
{
    /// <summary>Delivered.</summary>
    Sent,

    /// <summary>Edited by its sender.</summary>
    Edited,

    /// <summary>Deleted by its sender. Body is cleared; the row is retained.</summary>
    Deleted,
}

/// <summary>What produced a notification.</summary>
public enum NotificationKind
{
    /// <summary>Someone sent a friend request.</summary>
    FriendRequest,

    /// <summary>A friend request was accepted.</summary>
    FriendAccepted,

    /// <summary>Someone replied in a subscribed thread.</summary>
    ThreadReply,

    /// <summary>Someone reacted to a post.</summary>
    PostReaction,

    /// <summary>A direct message arrived.</summary>
    DirectMessage,

    /// <summary>A moderation decision affected the member.</summary>
    Moderation,
}

/// <summary>What a report is about.</summary>
public enum ReportTargetType
{
    /// <summary>A community profile.</summary>
    Profile,

    /// <summary>A forum thread.</summary>
    Thread,

    /// <summary>A forum post.</summary>
    Post,

    /// <summary>A direct message.</summary>
    Message,
}

/// <summary>Why something was reported.</summary>
public enum ReportReasonCode
{
    /// <summary>Unsolicited or repetitive promotion.</summary>
    Spam,

    /// <summary>Targeted abuse.</summary>
    Harassment,

    /// <summary>Hateful content.</summary>
    Hate,

    /// <summary>Sexual content.</summary>
    SexualContent,

    /// <summary>Violent content or threats.</summary>
    Violence,

    /// <summary>Pretending to be someone else.</summary>
    Impersonation,

    /// <summary>Exposure of private information.</summary>
    Privacy,

    /// <summary>Anything else, described in the detail field.</summary>
    Other,
}

/// <summary>Lifecycle of a report.</summary>
public enum ReportStatus
{
    /// <summary>Awaiting triage.</summary>
    Open,

    /// <summary>Being handled by a moderator.</summary>
    Reviewing,

    /// <summary>Actioned.</summary>
    Resolved,

    /// <summary>Reviewed and no action taken.</summary>
    Dismissed,
}

/// <summary>Why one member blocked another.</summary>
public enum BlockReasonCategory
{
    /// <summary>No reason given.</summary>
    Unspecified,

    /// <summary>Unwanted contact.</summary>
    Harassment,

    /// <summary>Unsolicited promotion.</summary>
    Spam,

    /// <summary>Personal preference.</summary>
    Personal,
}

/// <summary>How a member wants to hear about a thread.</summary>
public enum ThreadNotificationPreference
{
    /// <summary>Notify on every reply.</summary>
    AllReplies,

    /// <summary>Do not notify.</summary>
    None,
}
