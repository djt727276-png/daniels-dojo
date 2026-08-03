namespace DanielsDojo.Domain.Catalog;

/// <summary>
/// An ordered grouping of lessons within a course. Exposes the alternate key
/// (<see cref="CourseId"/>, <see cref="Id"/>) so <see cref="Lesson"/> can prove through a
/// composite foreign key that its section belongs to the same course.
/// </summary>
public sealed class CourseSection
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning course.</summary>
    public Guid CourseId { get; set; }

    /// <summary>Section title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional section description.</summary>
    public string? Description { get; set; }

    /// <summary>Position within the course. Unique per course.</summary>
    public int SortOrder { get; set; }

    /// <summary>Publication state.</summary>
    public PublicationStatus Status { get; set; } = PublicationStatus.Draft;

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The owning course.</summary>
    public Course? Course { get; set; }

    /// <summary>Lessons in this section.</summary>
    public ICollection<Lesson> Lessons { get; } = new List<Lesson>();
}
