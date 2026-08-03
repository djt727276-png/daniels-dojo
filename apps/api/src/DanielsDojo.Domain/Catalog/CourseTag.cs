namespace DanielsDojo.Domain.Catalog;

/// <summary>Assignment of a <see cref="Tag"/> to a <see cref="Course"/>.</summary>
public sealed class CourseTag
{
    /// <summary>Tagged course.</summary>
    public Guid CourseId { get; set; }

    /// <summary>Applied tag.</summary>
    public Guid TagId { get; set; }

    /// <summary>The tagged course.</summary>
    public Course? Course { get; set; }

    /// <summary>The applied tag.</summary>
    public Tag? Tag { get; set; }
}
