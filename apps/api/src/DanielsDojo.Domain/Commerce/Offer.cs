using DanielsDojo.Domain.Catalog;

namespace DanielsDojo.Domain.Commerce;

/// <summary>
/// Something a customer can buy. A <see cref="OfferKind.CourseLifetime"/> offer must name a
/// course; a <see cref="OfferKind.Membership"/> offer must not. Enforced by check constraint.
/// </summary>
public sealed class Offer
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Stable unique business code, for example "membership-monthly".</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name shown at checkout.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description shown at checkout.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>What the offer sells.</summary>
    public OfferKind Kind { get; set; }

    /// <summary>Course sold, required for course-lifetime offers and null otherwise.</summary>
    public Guid? CourseId { get; set; }

    /// <summary>Provider product identifier. Unique when present; null until created there.</summary>
    public string? StripeProductId { get; set; }

    /// <summary>Lifecycle state.</summary>
    public CommerceStatus Status { get; set; } = CommerceStatus.Draft;

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The course sold, for course-lifetime offers.</summary>
    public Course? Course { get; set; }

    /// <summary>Prices published for this offer. Superseded prices are retired, not deleted.</summary>
    public ICollection<Price> Prices { get; } = new List<Price>();
}
