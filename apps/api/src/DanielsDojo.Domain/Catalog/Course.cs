namespace DanielsDojo.Domain.Catalog;

/// <summary>A sellable course. Archived courses are retained for existing purchases.</summary>
public sealed class Course
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Unique URL segment, for example "atlas-enterprise-developer".</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Course title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Short catalog summary.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Long-form description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Blob object name for the cover image. Never a SAS URL.</summary>
    public string? ImageStorageKey { get; set; }

    /// <summary>Accessible alternative text for the cover image.</summary>
    public string? ImageAltText { get; set; }

    /// <summary>Difficulty banding.</summary>
    public CourseLevel Level { get; set; } = CourseLevel.AllLevels;

    /// <summary>Publication state.</summary>
    public PublicationStatus Status { get; set; } = PublicationStatus.Draft;

    /// <summary>Whether an active membership grants access to this course.</summary>
    public bool IncludedInMembership { get; set; }

    /// <summary>First publication instant, stored UTC.</summary>
    public DateTimeOffset? PublishedAtUtc { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Sections belonging to this course.</summary>
    public ICollection<CourseSection> Sections { get; } = new List<CourseSection>();

    /// <summary>Lessons belonging to this course.</summary>
    public ICollection<Lesson> Lessons { get; } = new List<Lesson>();

    /// <summary>Tag assignments.</summary>
    public ICollection<CourseTag> CourseTags { get; } = new List<CourseTag>();

    /// <summary>Instructor assignments. Does not grant a role.</summary>
    public ICollection<CourseInstructor> Instructors { get; } = new List<CourseInstructor>();
}
