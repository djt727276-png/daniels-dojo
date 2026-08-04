using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Community;

/// <summary>
/// A one-to-one conversation, stored as a canonical pair so the same two members always
/// resolve to the same conversation regardless of who opens it.
/// </summary>
public sealed class DirectConversation
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Lower half of the canonical participant pair.</summary>
    public Guid UserLowId { get; set; }

    /// <summary>Upper half of the canonical participant pair.</summary>
    public Guid UserHighId { get; set; }

    /// <summary>Most recent message instant, used for ordering. Stored UTC.</summary>
    public DateTimeOffset? LastMessageAtUtc { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Lower participant.</summary>
    public User? UserLow { get; set; }

    /// <summary>Upper participant.</summary>
    public User? UserHigh { get; set; }

    /// <summary>Messages in this conversation.</summary>
    public ICollection<DirectMessage> Messages { get; } = new List<DirectMessage>();

    /// <summary>Whether the given member is one of the two participants.</summary>
    public bool Includes(Guid userId) => UserLowId == userId || UserHighId == userId;
}

/// <summary>
/// A private message. The body is plain text and is cleared on delete, leaving a tombstone
/// so the conversation keeps its shape without retaining the content.
/// </summary>
public sealed class DirectMessage
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning conversation.</summary>
    public Guid ConversationId { get; set; }

    /// <summary>Member who sent the message.</summary>
    public Guid SenderUserId { get; set; }

    /// <summary>Plain-text body.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Lifecycle state.</summary>
    public DirectMessageStatus Status { get; set; } = DirectMessageStatus.Sent;

    /// <summary>When the sender last edited the message, stored UTC.</summary>
    public DateTimeOffset? EditedAtUtc { get; set; }

    /// <summary>When the sender deleted the message, stored UTC.</summary>
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The owning conversation.</summary>
    public DirectConversation? Conversation { get; set; }

    /// <summary>The sender.</summary>
    public User? Sender { get; set; }
}

/// <summary>How far one member has read in one conversation.</summary>
public sealed class ConversationReadState
{
    /// <summary>Conversation being tracked.</summary>
    public Guid ConversationId { get; set; }

    /// <summary>Member whose position this is.</summary>
    public Guid UserId { get; set; }

    /// <summary>Last message the member read, when any.</summary>
    public Guid? LastReadMessageId { get; set; }

    /// <summary>When the member last read, stored UTC.</summary>
    public DateTimeOffset? LastReadAtUtc { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>The conversation.</summary>
    public DirectConversation? Conversation { get; set; }

    /// <summary>The member.</summary>
    public User? User { get; set; }
}
