using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Community;

/// <summary>
/// A message within a thread.
/// </summary>
/// <remarks>
/// <see cref="Body"/> is plain text. Nothing in the platform renders it as HTML or Markdown,
/// and no attachment or embedded media is accepted, so a post cannot carry active content.
/// Removal is a tombstone: the row survives with its body cleared so replies keep their
/// position and the moderation decision stays on record.
/// </remarks>
public sealed class ForumPost
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning thread.</summary>
    public Guid ThreadId { get; set; }

    /// <summary>Member who wrote the post.</summary>
    public Guid AuthorUserId { get; set; }

    /// <summary>
    /// Optional post being replied to. Constrained to the same thread by a composite
    /// foreign key so a reply can never point outside its conversation.
    /// </summary>
    public Guid? ReplyToPostId { get; set; }

    /// <summary>Plain-text body.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Lifecycle state.</summary>
    public ForumPostStatus Status { get; set; } = ForumPostStatus.Published;

    /// <summary>When the author last edited the post, stored UTC.</summary>
    public DateTimeOffset? EditedAtUtc { get; set; }

    /// <summary>When the post was tombstoned, stored UTC.</summary>
    public DateTimeOffset? RemovedAtUtc { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The owning thread.</summary>
    public ForumThread? Thread { get; set; }

    /// <summary>The author.</summary>
    public User? Author { get; set; }

    /// <summary>The post being replied to.</summary>
    public ForumPost? ReplyToPost { get; set; }

    /// <summary>Reactions on this post.</summary>
    public ICollection<ForumPostReaction> Reactions { get; } = new List<ForumPostReaction>();
}
