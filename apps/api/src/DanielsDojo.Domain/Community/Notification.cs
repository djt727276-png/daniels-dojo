using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Community;

/// <summary>
/// An entry in a member's notification inbox.
/// </summary>
/// <remarks>
/// Deliberately carries no message body, post excerpt, or other private content — only a kind
/// and a pointer to the target. The client re-fetches the target through the ordinary
/// endpoints, which re-apply every privacy, friendship, and block rule. A notification row can
/// therefore never become a way to read something the member has since lost access to.
/// </remarks>
public sealed class Notification
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Member the notification belongs to.</summary>
    public Guid RecipientUserId { get; set; }

    /// <summary>Member who caused it, when there is one.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>What produced the notification.</summary>
    public NotificationKind Kind { get; set; }

    /// <summary>Type name of the target, for example "Thread".</summary>
    public string TargetType { get; set; } = string.Empty;

    /// <summary>Identifier of the target.</summary>
    public Guid TargetId { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>When the recipient read it, stored UTC. Null while unread.</summary>
    public DateTimeOffset? ReadAtUtc { get; set; }

    /// <summary>The recipient.</summary>
    public User? Recipient { get; set; }

    /// <summary>The actor.</summary>
    public User? Actor { get; set; }
}
