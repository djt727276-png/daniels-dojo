using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Community;

/// <summary>
/// One member's reaction to one post. The (post, user, type) triple is unique, so a
/// repeated tap cannot inflate a count.
/// </summary>
public sealed class ForumPostReaction
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Post reacted to.</summary>
    public Guid PostId { get; set; }

    /// <summary>Reacting member.</summary>
    public Guid UserId { get; set; }

    /// <summary>Kind of reaction. Only <see cref="ReactionType.Like"/> at launch.</summary>
    public ReactionType ReactionType { get; set; } = ReactionType.Like;

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>The post.</summary>
    public ForumPost? Post { get; set; }

    /// <summary>The reacting member.</summary>
    public User? User { get; set; }
}

/// <summary>
/// A member's interest in a thread, used to decide who is notified about replies.
/// </summary>
public sealed class ForumSubscription
{
    /// <summary>Subscribed thread.</summary>
    public Guid ThreadId { get; set; }

    /// <summary>Subscribing member.</summary>
    public Guid UserId { get; set; }

    /// <summary>How the member wants to hear about replies.</summary>
    public ThreadNotificationPreference NotificationPreference { get; set; } =
        ThreadNotificationPreference.AllReplies;

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>The thread.</summary>
    public ForumThread? Thread { get; set; }

    /// <summary>The subscribing member.</summary>
    public User? User { get; set; }
}
