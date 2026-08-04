using DanielsDojo.Domain.Media;

namespace DanielsDojo.Application.Commerce;

/// <summary>Configuration for the payment provider.</summary>
/// <remarks>
/// The same explicit-mode rule as media: the mode is read literally and never inferred from
/// whether a key happens to be present, because a deployment that silently swapped real
/// payments for the deterministic adapter would take money nowhere while looking like it had.
/// </remarks>
public sealed class PaymentProviderOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Commerce:Stripe";

    /// <summary>Which adapter serves payments.</summary>
    public ProviderMode Mode { get; set; } = ProviderMode.Disabled;

    /// <summary>Secret API key. Test key in development, live key only in production.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Webhook signing secret for inbound event verification.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Where Checkout returns the customer after success.</summary>
    public string SuccessUrl { get; set; } = "http://localhost:4200/my-learning?checkout=success";

    /// <summary>Where Checkout returns the customer after cancelling.</summary>
    public string CancelUrl { get; set; } = "http://localhost:4200/courses?checkout=cancelled";

    /// <summary>Where the Customer Portal returns the customer.</summary>
    public string PortalReturnUrl { get; set; } = "http://localhost:4200/account";
}

/// <summary>A hosted checkout the customer is redirected to.</summary>
/// <param name="SessionId">Provider session identifier, recorded before redirecting.</param>
/// <param name="Url">Where to send the customer. Never logged.</param>
public sealed record CheckoutTicket(string SessionId, Uri Url);

/// <summary>A hosted customer portal session.</summary>
/// <param name="Url">Where to send the customer. Never logged.</param>
public sealed record PortalTicket(Uri Url);

/// <summary>What the provider reports about a checkout session.</summary>
/// <param name="SessionId">Session identifier.</param>
/// <param name="IsPaid">Whether payment completed.</param>
/// <param name="CustomerId">Provider customer, once one exists.</param>
/// <param name="SubscriptionId">Provider subscription, for a recurring purchase.</param>
/// <param name="PaymentIntentId">Provider payment, for a one-time purchase.</param>
/// <param name="AmountTotalMinor">What was actually charged.</param>
/// <param name="Currency">Uppercase ISO currency.</param>
public sealed record CheckoutState(
    string SessionId,
    bool IsPaid,
    string? CustomerId,
    string? SubscriptionId,
    string? PaymentIntentId,
    long? AmountTotalMinor,
    string? Currency);

/// <summary>What the provider reports about a subscription.</summary>
/// <param name="SubscriptionId">Provider subscription identifier.</param>
/// <param name="Status">Provider status string, mapped by the caller.</param>
/// <param name="CurrentPeriodStart">Paid period start.</param>
/// <param name="CurrentPeriodEnd">Paid-through boundary.</param>
/// <param name="CancelAtPeriodEnd">Whether it ends at that boundary.</param>
/// <param name="CanceledAt">When cancellation was requested, if it was.</param>
/// <param name="EndedAt">When it fully ended, if it has.</param>
public sealed record SubscriptionState(
    string SubscriptionId,
    string Status,
    DateTimeOffset CurrentPeriodStart,
    DateTimeOffset CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CanceledAt,
    DateTimeOffset? EndedAt);

/// <summary>A verified inbound payment event.</summary>
/// <param name="EventId">Provider event identifier, used to reject replays.</param>
/// <param name="EventType">Provider event type.</param>
/// <param name="OccurredAtUtc">When the provider says it happened.</param>
/// <param name="SessionId">Checkout session the event concerns, when it names one.</param>
/// <param name="SubscriptionId">Subscription the event concerns, when it names one.</param>
/// <param name="PaymentIntentId">Payment the event concerns, when it names one.</param>
/// <param name="RefundId">Refund the event concerns, when it names one.</param>
/// <param name="DisputeId">Dispute the event concerns, when it names one.</param>
public sealed record PaymentProviderEvent(
    string EventId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string? SessionId,
    string? SubscriptionId,
    string? PaymentIntentId,
    string? RefundId,
    string? DisputeId);

/// <summary>
/// The payment provider.
/// </summary>
/// <remarks>
/// <para>
/// Everything access-granting flows through verification: a browser redirect saying "success"
/// triggers a read of the session from the provider, and an entitlement is written only from
/// what the provider itself reports. The redirect is a hint, never evidence.
/// </para>
/// <para>
/// The deterministic adapter is a genuine little payment machine — sessions it creates can be
/// completed, read back, and notified about — so the entire purchase path is testable with no
/// network and no test keys.
/// </para>
/// </remarks>
public interface IPaymentProvider
{
    /// <summary>Which adapter is serving.</summary>
    ProviderMode Mode { get; }

    /// <summary>Finds or creates the provider customer for a local user.</summary>
    Task<string> EnsureCustomerAsync(
        Guid userId,
        string email,
        string displayName,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a hosted checkout for one price.</summary>
    /// <param name="isRecurring">Subscription checkout when true; one-time payment otherwise.</param>
    /// <param name="clientReference">
    /// The local order or intent identifier, echoed back on the session and its events so a
    /// webhook can find its way home without trusting anything client-supplied.
    /// </param>
    Task<CheckoutTicket> CreateCheckoutAsync(
        string customerId,
        string providerPriceId,
        bool isRecurring,
        string clientReference,
        CancellationToken cancellationToken = default);

    /// <summary>Reads what the provider holds for a checkout session.</summary>
    Task<CheckoutState?> GetCheckoutAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Reads what the provider holds for a subscription.</summary>
    Task<SubscriptionState?> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a hosted customer portal session.</summary>
    Task<PortalTicket> CreatePortalAsync(string customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies an inbound event signature and parses it, or returns null when it does not
    /// hold up. A rejected delivery changes nothing and is not logged with its payload.
    /// </summary>
    PaymentProviderEvent? VerifyEvent(string payload, string? signatureHeader, DateTimeOffset now);

    /// <summary>
    /// Ensures a product and price exist at the provider for a local price, returning the
    /// provider price identifier. Idempotent.
    /// </summary>
    Task<(string ProductId, string PriceId)> EnsurePriceAsync(
        string offerCode,
        string offerName,
        Guid localPriceId,
        long amountMinor,
        string currency,
        bool isRecurring,
        CancellationToken cancellationToken = default);
}
