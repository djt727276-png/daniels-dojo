using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Commerce;

/// <summary>
/// A recurring membership at the payment provider. History is preserved: a user may hold
/// many subscription rows over time, so there is deliberately no uniqueness rule on
/// (UserId, OfferId). Trials exist in the model but are not enabled at launch.
/// </summary>
public sealed class Subscription
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Subscribing user. Restrictive: subscription history outlives account changes.</summary>
    public Guid UserId { get; set; }

    /// <summary>Offer subscribed to.</summary>
    public Guid OfferId { get; set; }

    /// <summary>Price charged.</summary>
    public Guid PriceId { get; set; }

    /// <summary>Provider subscription identifier. Unique.</summary>
    public string StripeSubscriptionId { get; set; } = string.Empty;

    /// <summary>Lifecycle state mirrored from the provider.</summary>
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Incomplete;

    /// <summary>Start of the current billing period, stored UTC.</summary>
    public DateTimeOffset CurrentPeriodStartUtc { get; set; }

    /// <summary>End of the current billing period, stored UTC.</summary>
    public DateTimeOffset CurrentPeriodEndUtc { get; set; }

    /// <summary>Whether the subscription ends when the current period closes.</summary>
    public bool CancelAtPeriodEnd { get; set; }

    /// <summary>When cancellation was requested, stored UTC.</summary>
    public DateTimeOffset? CanceledAtUtc { get; set; }

    /// <summary>When the subscription actually ended, stored UTC.</summary>
    public DateTimeOffset? EndedAtUtc { get; set; }

    /// <summary>Trial start. Trials are not enabled at launch.</summary>
    public DateTimeOffset? TrialStartUtc { get; set; }

    /// <summary>Trial end. Trials are not enabled at launch.</summary>
    public DateTimeOffset? TrialEndUtc { get; set; }

    /// <summary>When the first payment failure was observed, stored UTC.</summary>
    public DateTimeOffset? FirstPaymentFailedAtUtc { get; set; }

    /// <summary>When the dunning grace period ends, stored UTC.</summary>
    public DateTimeOffset? GracePeriodEndsAtUtc { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token; provider events update this row.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The subscribing user.</summary>
    public User? User { get; set; }

    /// <summary>The subscribed offer.</summary>
    public Offer? Offer { get; set; }

    /// <summary>The price charged.</summary>
    public Price? Price { get; set; }
}
