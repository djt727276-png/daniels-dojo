namespace DanielsDojo.Application.Commerce;

/// <summary>What a customer asks to buy.</summary>
/// <param name="OfferId">The offer being purchased.</param>
public sealed record StartCheckoutRequest(Guid OfferId);

/// <summary>Where to send the customer to pay.</summary>
/// <param name="CheckoutUrl">The hosted checkout. Used once, never logged.</param>
public sealed record CheckoutStarted(Uri CheckoutUrl);

/// <summary>Where to send the customer to manage their billing.</summary>
/// <param name="PortalUrl">The hosted portal. Used once, never logged.</param>
public sealed record PortalStarted(Uri PortalUrl);

/// <summary>
/// The outcome of confirming a checkout after the browser returned.
/// </summary>
/// <param name="Confirmed">
/// Whether the provider itself reports the session paid. A false here is not an error — the
/// customer may simply have not finished paying yet.
/// </param>
/// <param name="EntitlementGranted">Whether access was written as a result.</param>
public sealed record CheckoutConfirmed(bool Confirmed, bool EntitlementGranted);

/// <summary>One order in the customer's history.</summary>
/// <param name="Id">Order identifier.</param>
/// <param name="Status">Order status.</param>
/// <param name="TotalMinor">Total charged, in minor units.</param>
/// <param name="Currency">Uppercase ISO currency.</param>
/// <param name="OfferName">What was bought.</param>
/// <param name="PaidAtUtc">When it was paid.</param>
public sealed record OrderSummary(
    Guid Id,
    string Status,
    long TotalMinor,
    string Currency,
    string OfferName,
    DateTimeOffset? PaidAtUtc);

/// <summary>The customer's membership, as verified provider state.</summary>
/// <param name="Status">Subscription status.</param>
/// <param name="CurrentPeriodEndUtc">Paid-through boundary.</param>
/// <param name="CancelAtPeriodEnd">Whether it ends at that boundary.</param>
public sealed record MembershipSummary(
    string Status,
    DateTimeOffset CurrentPeriodEndUtc,
    bool CancelAtPeriodEnd);

/// <summary>The customer's commerce standing, for the account screen.</summary>
/// <param name="Membership">Active membership, when one exists.</param>
/// <param name="Orders">Order history, newest first.</param>
public sealed record BillingOverview(
    MembershipSummary? Membership,
    IReadOnlyList<OrderSummary> Orders);

/// <summary>Stable error codes for the commerce surface.</summary>
public static class CommerceErrorCodes
{
    /// <summary>The payment provider is switched off in this environment.</summary>
    public const string ProviderDisabled = "commerce.provider_disabled";

    /// <summary>The offer is not active or has no active price.</summary>
    public const string OfferUnavailable = "commerce.offer_unavailable";

    /// <summary>The customer already holds what this offer grants.</summary>
    public const string AlreadyOwned = "commerce.already_owned";

    /// <summary>No provider customer exists for this user yet.</summary>
    public const string NoBillingProfile = "commerce.no_billing_profile";

    /// <summary>The checkout session was not found or does not belong to this customer.</summary>
    public const string CheckoutNotFound = "commerce.checkout_not_found";
}
