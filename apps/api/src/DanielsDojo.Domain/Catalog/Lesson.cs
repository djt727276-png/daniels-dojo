namespace DanielsDojo.Domain.Catalog;

/// <summary>
/// A single lesson. Both <see cref="CourseId"/> and <see cref="CourseSectionId"/> are stored
/// so a composite foreign key can prove the section belongs to the same course; a lesson can
/// never be attached to a section owned by a different course.
/// </summary>
public sealed class Lesson
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning course.</summary>
    public Guid CourseId { get; set; }

    /// <summary>Owning section, which must belong to <see cref="CourseId"/>.</summary>
    public Guid CourseSectionId { get; set; }

    /// <summary>URL segment, unique within the course.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Lesson title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional short summary.</summary>
    public string? Summary { get; set; }

    /// <summary>Whether this lesson is a video or an article.</summary>
    public LessonType LessonType { get; set; }

    /// <summary>Markdown body, used by article lessons.</summary>
    public string? BodyMarkdown { get; set; }

    /// <summary>Position within the section. Unique per section.</summary>
    public int SortOrder { get; set; }

    /// <summary>Whether the lesson is playable without an entitlement.</summary>
    public bool IsPreview { get; set; }

    /// <summary>Publication state.</summary>
    public PublicationStatus Status { get; set; } = PublicationStatus.Draft;

    /// <summary>Approximate duration used for catalog display.</summary>
    public int? EstimatedDurationSeconds { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The owning course.</summary>
    public Course? Course { get; set; }

    /// <summary>The owning section.</summary>
    public CourseSection? CourseSection { get; set; }

    /// <summary>Video metadata, present for video lessons.</summary>
    public LessonVideo? Video { get; set; }

    /// <summary>Downloadable resources attached to this lesson.</summary>
    public ICollection<LessonResource> Resources { get; } = new List<LessonResource>();
}
