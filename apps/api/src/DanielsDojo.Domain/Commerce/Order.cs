using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Commerce;

/// <summary>
/// A one-time purchase. Subscriptions are never represented as orders. Amounts are integer
/// minor units and the total must equal subtotal plus tax, enforced by check constraint.
/// </summary>
public sealed class Order
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Purchasing user. Restrictive: orders outlive account changes.</summary>
    public Guid UserId { get; set; }

    /// <summary>Lifecycle state.</summary>
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>Uppercase ISO-4217 currency code.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Sum of line totals in minor units.</summary>
    public long SubtotalMinor { get; set; }

    /// <summary>Tax in minor units.</summary>
    public long TaxMinor { get; set; }

    /// <summary>Charged total in minor units. Equals subtotal plus tax.</summary>
    public long TotalMinor { get; set; }

    /// <summary>Provider checkout session identifier. Unique when present.</summary>
    public string? StripeCheckoutSessionId { get; set; }

    /// <summary>Provider payment intent identifier. Unique when present.</summary>
    public string? StripePaymentIntentId { get; set; }

    /// <summary>When payment settled, stored UTC.</summary>
    public DateTimeOffset? PaidAtUtc { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token; provider events update this row.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The purchasing user.</summary>
    public User? User { get; set; }

    /// <summary>Purchased lines.</summary>
    public ICollection<OrderItem> Items { get; } = new List<OrderItem>();
}
