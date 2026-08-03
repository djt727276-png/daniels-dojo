using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Learning;

/// <summary>
/// A student's declared participation in a course. Enrollment is an organisational and
/// progress-tracking concept only: it never grants access. Access comes solely from an
/// active <see cref="Commerce.Entitlement"/>.
/// </summary>
public sealed class Enrollment
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Enrolled user. Unique together with <see cref="CourseId"/>.</summary>
    public Guid UserId { get; set; }

    /// <summary>Course enrolled in.</summary>
    public Guid CourseId { get; set; }

    /// <summary>When the enrollment was created, stored UTC.</summary>
    public DateTimeOffset EnrolledAtUtc { get; set; }

    /// <summary>When the student last opened the course, stored UTC.</summary>
    public DateTimeOffset? LastAccessedAtUtc { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>The enrolled user.</summary>
    public User? User { get; set; }

    /// <summary>The course enrolled in.</summary>
    public Course? Course { get; set; }
}
