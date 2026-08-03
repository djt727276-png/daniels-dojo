using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Commerce;

/// <summary>
/// A recorded grant of access. Entitlements are the only thing that grants access —
/// enrollment does not. Scope and source are cross-checked by constraints: a Course scope
/// requires a course, a membership scope forbids one, a Subscription source carries only a
/// subscription, a Purchase source carries only an order item, and a Manual grant carries
/// neither commerce source. Revocation is recorded, never deleted.
/// </summary>
public sealed class Entitlement
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>User holding the grant.</summary>
    public Guid UserId { get; set; }

    /// <summary>What the grant covers.</summary>
    public EntitlementScope Scope { get; set; }

    /// <summary>Why the grant exists.</summary>
    public EntitlementSource Source { get; set; }

    /// <summary>Course covered, required for course scope and null otherwise.</summary>
    public Guid? CourseId { get; set; }

    /// <summary>Originating subscription, present only for subscription-sourced grants.</summary>
    public Guid? SubscriptionId { get; set; }

    /// <summary>Originating order line, present only for purchase-sourced grants.</summary>
    public Guid? OrderItemId { get; set; }

    /// <summary>Lifecycle state.</summary>
    public EntitlementStatus Status { get; set; } = EntitlementStatus.Active;

    /// <summary>When access begins, stored UTC.</summary>
    public DateTimeOffset StartsAtUtc { get; set; }

    /// <summary>When access ends. Must not precede <see cref="StartsAtUtc"/>.</summary>
    public DateTimeOffset? EndsAtUtc { get; set; }

    /// <summary>Administrator who created a manual grant.</summary>
    public Guid? GrantedByUserId { get; set; }

    /// <summary>Recorded justification for the grant.</summary>
    public string? GrantReason { get; set; }

    /// <summary>When the grant was revoked, stored UTC.</summary>
    public DateTimeOffset? RevokedAtUtc { get; set; }

    /// <summary>Administrator who revoked the grant.</summary>
    public Guid? RevokedByUserId { get; set; }

    /// <summary>Recorded justification for revocation.</summary>
    public string? RevocationReason { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token; administrators update this row.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The user holding the grant.</summary>
    public User? User { get; set; }

    /// <summary>The covered course, for course-scoped grants.</summary>
    public Course? Course { get; set; }

    /// <summary>The originating subscription.</summary>
    public Subscription? Subscription { get; set; }

    /// <summary>The originating order line.</summary>
    public OrderItem? OrderItem { get; set; }

    /// <summary>The administrator who granted a manual entitlement.</summary>
    public User? GrantedByUser { get; set; }

    /// <summary>The administrator who revoked the entitlement.</summary>
    public User? RevokedByUser { get; set; }
}
