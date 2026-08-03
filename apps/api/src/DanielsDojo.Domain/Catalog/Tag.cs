namespace DanielsDojo.Domain.Catalog;

/// <summary>A catalog tag used for grouping and filtering courses.</summary>
public sealed class Tag
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Upper-cased unique lookup name.</summary>
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Courses carrying this tag.</summary>
    public ICollection<CourseTag> CourseTags { get; } = new List<CourseTag>();
}
