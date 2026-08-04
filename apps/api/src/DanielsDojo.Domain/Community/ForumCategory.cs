namespace DanielsDojo.Domain.Community;

/// <summary>A top-level grouping of forum threads.</summary>
public sealed class ForumCategory
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Unique URL segment.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What belongs in this category.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Position in the category list.</summary>
    public int SortOrder { get; set; }

    /// <summary>Lifecycle state. Categories are archived, never deleted.</summary>
    public ForumCategoryStatus Status { get; set; } = ForumCategoryStatus.Active;

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Threads in this category.</summary>
    public ICollection<ForumThread> Threads { get; } = new List<ForumThread>();
}
