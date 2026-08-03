namespace DanielsDojo.Domain.Commerce;

/// <summary>What an offer sells.</summary>
public enum OfferKind
{
    /// <summary>All-access membership covering every course flagged for membership.</summary>
    Membership,

    /// <summary>Lifetime access to one specific course.</summary>
    CourseLifetime,
}

/// <summary>Lifecycle of a commerce record that is retired rather than deleted.</summary>
public enum CommerceStatus
{
    /// <summary>Being prepared; not purchasable.</summary>
    Draft,

    /// <summary>Purchasable.</summary>
    Active,

    /// <summary>Withdrawn. Retained because existing records reference it.</summary>
    Retired,
}

/// <summary>How often a price is charged.</summary>
public enum BillingInterval
{
    /// <summary>Charged once.</summary>
    OneTime,

    /// <summary>Charged every month.</summary>
    Month,
}

/// <summary>Lifecycle of a one-time order.</summary>
public enum OrderStatus
{
    /// <summary>Created but not yet paid.</summary>
    Pending,

    /// <summary>Paid in full.</summary>
    Paid,

    /// <summary>Payment failed.</summary>
    Failed,

    /// <summary>Some of the amount has been refunded.</summary>
    PartiallyRefunded,

    /// <summary>Fully refunded.</summary>
    Refunded,

    /// <summary>A dispute is open against the payment.</summary>
    Disputed,

    /// <summary>A dispute was resolved against the platform.</summary>
    ChargebackLost,
}

/// <summary>Lifecycle of a recurring subscription, mirroring provider states.</summary>
public enum SubscriptionStatus
{
    /// <summary>Initial payment has not completed.</summary>
    Incomplete,

    /// <summary>In a trial period. Trials are not enabled at launch.</summary>
    Trialing,

    /// <summary>Paid and current.</summary>
    Active,

    /// <summary>A payment failed and the grace period is running.</summary>
    PastDue,

    /// <summary>Payment retries were exhausted.</summary>
    Unpaid,

    /// <summary>Deliberately paused.</summary>
    Paused,

    /// <summary>Ended by the customer or an administrator.</summary>
    Canceled,

    /// <summary>The initial payment window expired.</summary>
    IncompleteExpired,
}

/// <summary>What an entitlement grants access to.</summary>
public enum EntitlementScope
{
    /// <summary>Every course flagged as included in membership.</summary>
    AllMembershipCourses,

    /// <summary>One specific course.</summary>
    Course,
}

/// <summary>Why an entitlement exists.</summary>
public enum EntitlementSource
{
    /// <summary>Granted by an active subscription.</summary>
    Subscription,

    /// <summary>Granted by a one-time purchase.</summary>
    Purchase,

    /// <summary>Granted manually by an administrator, with a recorded reason.</summary>
    Manual,
}

/// <summary>Lifecycle of an access grant.</summary>
public enum EntitlementStatus
{
    /// <summary>Currently grants access.</summary>
    Active,

    /// <summary>Withdrawn before its natural end, with a recorded reason.</summary>
    Revoked,

    /// <summary>Reached its end date.</summary>
    Expired,
}

/// <summary>Processing state of an inbound provider webhook event.</summary>
public enum WebhookEventStatus
{
    /// <summary>Recorded and awaiting processing.</summary>
    Received,

    /// <summary>Currently being processed.</summary>
    Processing,

    /// <summary>Processed successfully.</summary>
    Processed,

    /// <summary>Processing failed; a retry may be scheduled.</summary>
    Failed,

    /// <summary>Deliberately not actioned by this platform.</summary>
    Ignored,
}

/// <summary>Lifecycle of a refund at the payment provider.</summary>
public enum RefundStatus
{
    /// <summary>Submitted and awaiting settlement.</summary>
    Pending,

    /// <summary>Settled.</summary>
    Succeeded,

    /// <summary>Rejected by the provider.</summary>
    Failed,

    /// <summary>Withdrawn before settlement.</summary>
    Canceled,
}

/// <summary>Lifecycle of a payment dispute.</summary>
public enum PaymentDisputeStatus
{
    /// <summary>Early warning; a response may be required.</summary>
    WarningNeedsResponse,

    /// <summary>Evidence is required.</summary>
    NeedsResponse,

    /// <summary>Evidence submitted; awaiting the issuer.</summary>
    UnderReview,

    /// <summary>Resolved in the platform's favour.</summary>
    Won,

    /// <summary>Resolved against the platform.</summary>
    Lost,

    /// <summary>Closed with no further action.</summary>
    Closed,
}
