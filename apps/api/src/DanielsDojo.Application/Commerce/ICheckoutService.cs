using DanielsDojo.Application.Common;

namespace DanielsDojo.Application.Commerce;

/// <summary>
/// Customer purchasing: checkout, confirmation, billing portal, and history.
/// </summary>
/// <remarks>
/// The one rule that governs everything here: access comes from verified provider state,
/// never from a browser redirect or a client claim. Both the return-from-checkout path and
/// the webhook path converge on the same confirmation logic, so whichever arrives first wins
/// and the second is a no-op.
/// </remarks>
public interface ICheckoutService
{
    /// <summary>Starts a hosted checkout for one offer.</summary>
    Task<OperationResult<CheckoutStarted>> StartCheckoutAsync(
        Guid userId,
        StartCheckoutRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms a checkout by reading the session from the provider, and grants the
    /// entitlement when — and only when — the provider reports it paid.
    /// </summary>
    Task<OperationResult<CheckoutConfirmed>> ConfirmCheckoutAsync(
        Guid userId,
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Starts a hosted billing portal session.</summary>
    Task<OperationResult<PortalStarted>> StartPortalAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>The customer's own billing standing and order history.</summary>
    Task<OperationResult<BillingOverview>> GetBillingAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

/// <summary>Inbound payment provider notifications.</summary>
public interface ICommerceWebhookService
{
    /// <summary>
    /// Verifies and applies one notification. Returns true when the delivery was accepted,
    /// including when it was a duplicate — the provider should not retry something that was
    /// understood.
    /// </summary>
    Task<bool> HandlePaymentEventAsync(
        string payload,
        string? signatureHeader,
        CancellationToken cancellationToken = default);
}
