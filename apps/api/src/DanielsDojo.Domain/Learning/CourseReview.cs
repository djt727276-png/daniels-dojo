using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Learning;

/// <summary>Lifecycle of a review.</summary>
public enum CourseReviewStatus
{
    /// <summary>Visible and counted in the course aggregate.</summary>
    Published,

    /// <summary>Hidden by moderation. Kept for audit; never counted.</summary>
    Hidden,

    /// <summary>Withdrawn by its author. Kept as a tombstone; never counted.</summary>
    Deleted,
}

/// <summary>
/// One student's review of one course.
/// </summary>
/// <remarks>
/// Reviews come only from members who genuinely hold the course and have completed at least
/// one lesson of it — the enrolment gate lives in the service, and the progress threshold is
/// defined once there. One active review per member per course, enforced by the database.
/// Aggregates are computed from <see cref="CourseReviewStatus.Published"/> rows at read time,
/// so a hidden review leaves the average the moment it is hidden and no stored number can
/// drift from the truth.
/// </remarks>
public sealed class CourseReview
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>The reviewer.</summary>
    public Guid UserId { get; set; }

    /// <summary>The reviewed course.</summary>
    public Guid CourseId { get; set; }

    /// <summary>Stars, 1–5.</summary>
    public int Rating { get; set; }

    /// <summary>The written review.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Where the review is in its lifecycle.</summary>
    public CourseReviewStatus Status { get; set; } = CourseReviewStatus.Published;

    /// <summary>Why moderation hid it. Required when hidden.</summary>
    public string? ModerationReason { get; set; }

    /// <summary>When the author last edited it, shown as the edited indicator.</summary>
    public DateTimeOffset? EditedAtUtc { get; set; }

    /// <summary>Row creation instant.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Optimistic concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The reviewer.</summary>
    public User? User { get; set; }

    /// <summary>The course.</summary>
    public Course? Course { get; set; }
}
