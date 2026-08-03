using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Catalog;

/// <summary>
/// Attribution of a course to an instructor. This relation is presentational: it does
/// not grant the Instructor role and confers no authorization by itself.
/// </summary>
public sealed class CourseInstructor
{
    /// <summary>Attributed course.</summary>
    public Guid CourseId { get; set; }

    /// <summary>Attributed user.</summary>
    public Guid UserId { get; set; }

    /// <summary>When the attribution was made, stored UTC.</summary>
    public DateTimeOffset AssignedAtUtc { get; set; }

    /// <summary>The attributed course.</summary>
    public Course? Course { get; set; }

    /// <summary>The attributed user.</summary>
    public User? User { get; set; }
}
