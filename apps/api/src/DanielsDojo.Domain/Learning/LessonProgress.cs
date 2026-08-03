using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Learning;

/// <summary>
/// A student's progress through one lesson. Progress deliberately survives loss of access:
/// a lapsed membership hides the content but never erases the record.
/// </summary>
public sealed class LessonProgress
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Student. Unique together with <see cref="LessonId"/>.</summary>
    public Guid UserId { get; set; }

    /// <summary>Lesson being tracked.</summary>
    public Guid LessonId { get; set; }

    /// <summary>When the student first opened the lesson, stored UTC.</summary>
    public DateTimeOffset? StartedAtUtc { get; set; }

    /// <summary>
    /// When the lesson was completed, stored UTC. Required whenever the lesson counts as
    /// complete, enforced by check constraint.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Resume position in seconds. Never negative.</summary>
    public int LastPositionSeconds { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The student.</summary>
    public User? User { get; set; }

    /// <summary>The lesson being tracked.</summary>
    public Lesson? Lesson { get; set; }
}
