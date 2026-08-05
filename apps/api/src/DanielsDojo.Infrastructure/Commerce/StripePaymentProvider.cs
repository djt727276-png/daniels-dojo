using DanielsDojo.Application.Commerce;
using DanielsDojo.Domain.Media;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace DanielsDojo.Infrastructure.Commerce;

/// <summary>
/// Payments through Stripe.
/// </summary>
/// <remarks>
/// <para>
/// The secret key lives in options bound from user secrets or Key Vault; it is handed to a
/// per-instance <see cref="StripeClient"/> rather than the SDK's mutable global, so two
/// environments in one process could never cross keys. Nothing here logs a key, a URL with a
/// session in it, or a webhook payload.
/// </para>
/// <para>
/// Idempotency keys are derived from local identifiers on every mutating call, so a retried
/// request creates one checkout, not two.
/// </para>
/// </remarks>
internal sealed class StripePaymentProvider : IPaymentProvider
{
    private readonly StripeClient _client;
    private readonly PaymentProviderOptions _options;
    private readonly TimeProvider _timeProvider;

    public StripePaymentProvider(IOptions<PaymentProviderOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _client = new StripeClient(_options.SecretKey);
    }

    /// <inheritdoc />
    public ProviderMode Mode => ProviderMode.Real;

    /// <inheritdoc />
    public async Task<string> EnsureCustomerAsync(
        Guid userId,
        string email,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        // The local user identifier is the durable key; email is display metadata that can
        // change. Search first so a retried provisioning never mints a duplicate customer.
        StripeSearchResult<Customer> existing = await new CustomerService(_client).SearchAsync(
            new CustomerSearchOptions
            {
                Query = $"metadata['danielsDojoUserId']:'{userId:D}'",
                Limit = 1,
            },
            cancellationToken: cancellationToken);

        if (existing.Data.Count > 0)
        {
            return existing.Data[0].Id;
        }

        Customer created = await new CustomerService(_client).CreateAsync(
            new CustomerCreateOptions
            {
                Email = email,
                Name = displayName,
                Metadata = new Dictionary<string, string>
                {
                    ["danielsDojoUserId"] = userId.ToString("D"),
                },
            },
            new RequestOptions { IdempotencyKey = $"customer-{userId:D}" },
            cancellationToken);

        return created.Id;
    }

    /// <inheritdoc />
    public async Task<CheckoutTicket> CreateCheckoutAsync(
        string customerId,
        string providerPriceId,
        bool isRecurring,
        string clientReference,
        CancellationToken cancellationToken = default)
    {
        Session session = await new SessionService(_client).CreateAsync(
            new SessionCreateOptions
            {
                Customer = customerId,
                Mode = isRecurring ? "subscription" : "payment",
                ClientReferenceId = clientReference,
                LineItems =
                [
                    new SessionLineItemOptions { Price = providerPriceId, Quantity = 1 },
                ],
                SuccessUrl = $"{_options.SuccessUrl}&session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl = _options.CancelUrl,
            },
            new RequestOptions { IdempotencyKey = $"checkout-{clientReference}" },
            cancellationToken);

        return new CheckoutTicket(session.Id, new Uri(session.Url));
    }

    /// <inheritdoc />
    public async Task<CheckoutState?> GetCheckoutAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Session session = await new SessionService(_client).GetAsync(
                sessionId,
                cancellationToken: cancellationToken);

            return new CheckoutState(
                session.Id,
                string.Equals(session.PaymentStatus, "paid", StringComparison.Ordinal),
                session.CustomerId,
                session.SubscriptionId,
                session.PaymentIntentId,
                session.AmountTotal,
                session.Currency?.ToUpperInvariant());
        }
        catch (StripeException failure) when (failure.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<SubscriptionState?> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Subscription subscription = await new SubscriptionService(_client).GetAsync(
                subscriptionId,
                cancellationToken: cancellationToken);

            SubscriptionItem? item = subscription.Items?.Data?.FirstOrDefault();

            return new SubscriptionState(
                subscription.Id,
                subscription.Status,
                item?.CurrentPeriodStart ?? subscription.Created,
                item?.CurrentPeriodEnd ?? subscription.Created,
                subscription.CancelAtPeriodEnd,
                subscription.CanceledAt,
                subscription.EndedAt);
        }
        catch (StripeException failure) when (failure.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<PortalTicket> CreatePortalAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        Stripe.BillingPortal.Session session =
            await new Stripe.BillingPortal.SessionService(_client).CreateAsync(
                new Stripe.BillingPortal.SessionCreateOptions
                {
                    Customer = customerId,
                    ReturnUrl = _options.PortalReturnUrl,
                },
                cancellationToken: cancellationToken);

        return new PortalTicket(new Uri(session.Url));
    }

    /// <inheritdoc />
    public PaymentProviderEvent? VerifyEvent(string payload, string? signatureHeader, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret) || signatureHeader is null)
        {
            // No secret configured means no delivery can be trusted. Fail closed.
            return null;
        }

        try
        {
            // Stripe's own constructor checks the v1 HMAC and the timestamp tolerance
            // against the instant we pass in, keeping verification on the injected clock.
            Event verified = EventUtility.ConstructEvent(
                payload,
                signatureHeader,
                _options.WebhookSecret,
                tolerance: 300,
                utcNow: now.ToUnixTimeSeconds(),
                throwOnApiVersionMismatch: false);

            return StripeEventParser.Parse(payload) is { } parsed
                ? parsed with { EventId = verified.Id, EventType = verified.Type }
                : null;
        }
        catch (StripeException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<(string ProductId, string PriceId)> EnsurePriceAsync(
        string offerCode,
        string offerName,
        Guid localPriceId,
        long amountMinor,
        string currency,
        bool isRecurring,
        CancellationToken cancellationToken = default)
    {
        // Prices are immutable at Stripe just as they are locally, so the local price
        // identifier is a natural idempotency key: the same local price always resolves to the
        // same provider price, and a changed amount is a new local price with a new key.
        StripeSearchResult<Price> existing = await new PriceService(_client).SearchAsync(
            new PriceSearchOptions
            {
                Query = $"metadata['danielsDojoPriceId']:'{localPriceId:D}'",
                Limit = 1,
            },
            cancellationToken: cancellationToken);

        if (existing.Data.Count > 0)
        {
            return (existing.Data[0].ProductId, existing.Data[0].Id);
        }

        Product product = await new ProductService(_client).CreateAsync(
            new ProductCreateOptions
            {
                Name = offerName,
                Metadata = new Dictionary<string, string> { ["danielsDojoOfferCode"] = offerCode },
            },
            new RequestOptions { IdempotencyKey = $"product-{offerCode}" },
            cancellationToken);

        Price price = await new PriceService(_client).CreateAsync(
            new PriceCreateOptions
            {
                Product = product.Id,
                UnitAmount = amountMinor,
                Currency = currency.ToLowerInvariant(),
                Recurring = isRecurring
                    ? new PriceRecurringOptions { Interval = "month" }
                    : null,
                Metadata = new Dictionary<string, string>
                {
                    ["danielsDojoPriceId"] = localPriceId.ToString("D"),
                },
            },
            new RequestOptions { IdempotencyKey = $"price-{localPriceId:D}" },
            cancellationToken);

        return (product.Id, price.Id);
    }
}
