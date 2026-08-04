using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Community;

/// <summary>
/// A discussion topic. Threads are locked, archived, or tombstoned — never deleted — so
/// replies keep their context and moderation stays auditable.
/// </summary>
public sealed class ForumThread
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning category.</summary>
    public Guid CategoryId { get; set; }

    /// <summary>Optional course this thread is about.</summary>
    public Guid? CourseId { get; set; }

    /// <summary>Member who started the thread.</summary>
    public Guid AuthorUserId { get; set; }

    /// <summary>Thread title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Lifecycle state.</summary>
    public ForumThreadStatus Status { get; set; } = ForumThreadStatus.Open;

    /// <summary>Whether the thread is pinned to the top of its category.</summary>
    public bool IsPinned { get; set; }

    /// <summary>Most recent post instant, used for ordering. Stored UTC.</summary>
    public DateTimeOffset LastActivityAtUtc { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The owning category.</summary>
    public ForumCategory? Category { get; set; }

    /// <summary>The optional related course.</summary>
    public Course? Course { get; set; }

    /// <summary>The member who started the thread.</summary>
    public User? Author { get; set; }

    /// <summary>Posts in this thread.</summary>
    public ICollection<ForumPost> Posts { get; } = new List<ForumPost>();

    /// <summary>Whether ordinary members may currently reply.</summary>
    public bool AcceptsReplies => Status == ForumThreadStatus.Open;
}
