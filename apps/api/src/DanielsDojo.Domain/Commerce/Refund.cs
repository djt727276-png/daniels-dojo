namespace DanielsDojo.Domain.Commerce;

/// <summary>
/// A refund issued at the payment provider. Exactly one source is set — either an order or
/// a subscription — enforced by check constraint. Partial refunds set
/// <see cref="RequiresAccessReview"/> for a human decision; access is never revoked by a
/// percentage rule.
/// </summary>
public sealed class Refund
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Provider refund identifier. Unique.</summary>
    public string StripeRefundId { get; set; } = string.Empty;

    /// <summary>Refunded order, when the refund relates to a one-time purchase.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Refunded subscription, when the refund relates to a membership.</summary>
    public Guid? SubscriptionId { get; set; }

    /// <summary>Provider payment intent the refund was issued against.</summary>
    public string StripePaymentIntentId { get; set; } = string.Empty;

    /// <summary>Refunded amount in minor units.</summary>
    public long AmountMinor { get; set; }

    /// <summary>Uppercase ISO-4217 currency code.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Provider settlement state.</summary>
    public RefundStatus Status { get; set; } = RefundStatus.Pending;

    /// <summary>Recorded reason for the refund.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Whether the full charge was refunded.</summary>
    public bool IsFullRefund { get; set; }

    /// <summary>Whether an administrator must review the customer's access.</summary>
    public bool RequiresAccessReview { get; set; }

    /// <summary>When the refund occurred, stored UTC.</summary>
    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token; provider events update this row.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The refunded order.</summary>
    public Order? Order { get; set; }

    /// <summary>The refunded subscription.</summary>
    public Subscription? Subscription { get; set; }
}
