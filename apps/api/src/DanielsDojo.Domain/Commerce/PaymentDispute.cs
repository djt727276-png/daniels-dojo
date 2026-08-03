namespace DanielsDojo.Domain.Commerce;

/// <summary>
/// A chargeback or dispute raised against a payment. Exactly one source is set — either an
/// order or a subscription — enforced by check constraint.
/// </summary>
public sealed class PaymentDispute
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Provider dispute identifier. Unique.</summary>
    public string StripeDisputeId { get; set; } = string.Empty;

    /// <summary>Provider charge the dispute was raised against.</summary>
    public string StripeChargeId { get; set; } = string.Empty;

    /// <summary>Disputed order, when the dispute relates to a one-time purchase.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Disputed subscription, when the dispute relates to a membership.</summary>
    public Guid? SubscriptionId { get; set; }

    /// <summary>Disputed amount in minor units.</summary>
    public long AmountMinor { get; set; }

    /// <summary>Uppercase ISO-4217 currency code.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Dispute lifecycle state.</summary>
    public PaymentDisputeStatus Status { get; set; } = PaymentDisputeStatus.NeedsResponse;

    /// <summary>Reason reported by the provider.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>When the dispute closed, stored UTC.</summary>
    public DateTimeOffset? ResolvedAtUtc { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token; provider events update this row.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The disputed order.</summary>
    public Order? Order { get; set; }

    /// <summary>The disputed subscription.</summary>
    public Subscription? Subscription { get; set; }
}
