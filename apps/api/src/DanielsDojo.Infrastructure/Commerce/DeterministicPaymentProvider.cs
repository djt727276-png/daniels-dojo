using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DanielsDojo.Application.Commerce;
using DanielsDojo.Domain.Media;
using DanielsDojo.Infrastructure.Media;
using Microsoft.Extensions.Options;

namespace DanielsDojo.Infrastructure.Commerce;

/// <summary>
/// A payment provider that behaves like the real one without leaving the process.
/// </summary>
/// <remarks>
/// <para>
/// It is a genuine little payment machine, not canned responses: a checkout it creates starts
/// unpaid, must be explicitly completed by the test acting as "the customer paid", and only
/// then reads back as paid. That means the code path that refuses to grant access from an
/// unpaid session is exercised for real, which is the path that matters.
/// </para>
/// <para>
/// Notifications are signed and verified with the same scheme and the same code as the real
/// adapter, so a signature-checking regression fails deterministically.
/// </para>
/// </remarks>
public sealed class DeterministicPaymentProvider(
    IOptions<PaymentProviderOptions> options,
    TimeProvider timeProvider) : IPaymentProvider
{
    /// <summary>Secret the deterministic adapter signs and verifies notifications with.</summary>
    public const string DeterministicWebhookSecret = "deterministic-payment-webhook-secret";

    private readonly ConcurrentDictionary<string, DeterministicCheckout> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SubscriptionState> _subscriptions = new(StringComparer.Ordinal);
    private readonly PaymentProviderOptions _options = options.Value;

    /// <inheritdoc />
    public ProviderMode Mode => ProviderMode.Deterministic;

    /// <inheritdoc />
    public Task<string> EnsureCustomerAsync(
        Guid userId,
        string email,
        string displayName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult($"cus_det_{Derive(userId.ToString("N"))}");

    /// <inheritdoc />
    public Task<CheckoutTicket> CreateCheckoutAsync(
        string customerId,
        string providerPriceId,
        bool isRecurring,
        string clientReference,
        CancellationToken cancellationToken = default)
    {
        string sessionId = $"cs_det_{Derive(clientReference)}";

        _sessions[sessionId] = new DeterministicCheckout(
            sessionId, customerId, providerPriceId, isRecurring, clientReference, IsPaid: false);

        return Task.FromResult(new CheckoutTicket(
            sessionId,
            new Uri($"/checkout/deterministic/{sessionId}", UriKind.Relative)));
    }

    /// <inheritdoc />
    public Task<CheckoutState?> GetCheckoutAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out DeterministicCheckout? session))
        {
            return Task.FromResult<CheckoutState?>(null);
        }

        return Task.FromResult<CheckoutState?>(new CheckoutState(
            session.SessionId,
            session.IsPaid,
            session.CustomerId,
            session is { IsPaid: true, IsRecurring: true } ? $"sub_det_{Derive(sessionId)}" : null,
            session is { IsPaid: true, IsRecurring: false } ? $"pi_det_{Derive(sessionId)}" : null,
            session.IsPaid ? 999 : null,
            session.IsPaid ? "USD" : null));
    }

    /// <inheritdoc />
    public Task<SubscriptionState?> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        return Task.FromResult<SubscriptionState?>(
            _subscriptions.GetValueOrDefault(subscriptionId)
            ?? new SubscriptionState(
                subscriptionId, "active", now, now.AddMonths(1),
                CancelAtPeriodEnd: false, CanceledAt: null, EndedAt: null));
    }

    /// <inheritdoc />
    public Task<PortalTicket> CreatePortalAsync(
        string customerId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PortalTicket(new Uri("/account/deterministic-portal", UriKind.Relative)));

    /// <inheritdoc />
    public PaymentProviderEvent? VerifyEvent(string payload, string? signatureHeader, DateTimeOffset now)
    {
        string secret = string.IsNullOrWhiteSpace(_options.WebhookSecret)
            ? DeterministicWebhookSecret
            : _options.WebhookSecret;

        if (!ProviderSignatures.IsValidWebhookSignature(
            payload, signatureHeader, secret, now, TimeSpan.FromMinutes(5)))
        {
            return null;
        }

        return StripeEventParser.Parse(payload);
    }

    /// <inheritdoc />
    public Task<(string ProductId, string PriceId)> EnsurePriceAsync(
        string offerCode,
        string offerName,
        Guid localPriceId,
        long amountMinor,
        string currency,
        bool isRecurring,
        CancellationToken cancellationToken = default) =>
        Task.FromResult((
            $"prod_det_{Derive(offerCode)}",
            $"price_det_{Derive(localPriceId.ToString("N"))}"));

    // ------------------------------------------------------------------ test surface

    /// <summary>Marks a session paid — the deterministic stand-in for "the customer paid".</summary>
    public bool CompleteCheckout(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out DeterministicCheckout? session))
        {
            return false;
        }

        _sessions[sessionId] = session with { IsPaid = true };

        if (session.IsRecurring)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();

            _subscriptions[$"sub_det_{Derive(sessionId)}"] = new SubscriptionState(
                $"sub_det_{Derive(sessionId)}", "active", now, now.AddMonths(1),
                CancelAtPeriodEnd: false, CanceledAt: null, EndedAt: null);
        }

        return true;
    }

    /// <summary>Overwrites a subscription's reported state, for lapse and cancellation tests.</summary>
    public void SetSubscription(SubscriptionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _subscriptions[state.SubscriptionId] = state;
    }

    /// <summary>Builds the signed notification the provider would send for a paid session.</summary>
    public (string Payload, string Signature) CreateCheckoutCompletedNotification(string sessionId)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        string payload = JsonSerializer.Serialize(new
        {
            id = $"evt_det_{Derive($"{sessionId}:completed")}",
            type = "checkout.session.completed",
            created = now.ToUnixTimeSeconds(),
            data = new { @object = new { id = sessionId, @object = "checkout.session" } },
        });

        string secret = string.IsNullOrWhiteSpace(_options.WebhookSecret)
            ? DeterministicWebhookSecret
            : _options.WebhookSecret;

        return (payload, ProviderSignatures.CreateWebhookSignature(payload, secret, now));
    }

    private sealed record DeterministicCheckout(
        string SessionId,
        string CustomerId,
        string ProviderPriceId,
        bool IsRecurring,
        string ClientReference,
        bool IsPaid);

    private static string Derive(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..20]
            .ToLowerInvariant();
}

/// <summary>
/// Reads payment provider payloads into the application's vocabulary.
/// </summary>
/// <remarks>
/// Shared by both adapters. Anything unrecognised produces null rather than an exception —
/// a changed payload shape is an operational fact, not a crash on a public endpoint.
/// </remarks>
internal static class StripeEventParser
{
    /// <summary>Parses a notification body, or null when it is not one we can apply safely.</summary>
    public static PaymentProviderEvent? Parse(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || ReadString(root, "id") is not { Length: > 0 } eventId
                || ReadString(root, "type") is not { Length: > 0 } eventType)
            {
                return null;
            }

            DateTimeOffset occurredAt =
                root.TryGetProperty("created", out JsonElement created)
                && created.ValueKind == JsonValueKind.Number
                    ? DateTimeOffset.FromUnixTimeSeconds(created.GetInt64())
                    : DateTimeOffset.MinValue;

            string? sessionId = null;
            string? subscriptionId = null;
            string? paymentIntentId = null;
            string? refundId = null;
            string? disputeId = null;

            if (root.TryGetProperty("data", out JsonElement data)
                && data.TryGetProperty("object", out JsonElement target)
                && target.ValueKind == JsonValueKind.Object)
            {
                string? objectType = ReadString(target, "object");
                string? objectId = ReadString(target, "id");

                switch (objectType)
                {
                    case "checkout.session":
                        sessionId = objectId;
                        subscriptionId = ReadString(target, "subscription");
                        break;
                    case "subscription":
                        subscriptionId = objectId;
                        break;
                    case "refund":
                        refundId = objectId;
                        paymentIntentId = ReadString(target, "payment_intent");
                        break;
                    case "dispute":
                        disputeId = objectId;
                        paymentIntentId = ReadString(target, "payment_intent");
                        break;
                    default:
                        paymentIntentId = ReadString(target, "payment_intent");
                        break;
                }
            }

            return new PaymentProviderEvent(
                eventId, eventType, occurredAt,
                sessionId, subscriptionId, paymentIntentId, refundId, disputeId);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Maps a provider subscription status string onto the domain enum's name.</summary>
    public static string MapSubscriptionStatus(string providerStatus) =>
        providerStatus.ToUpperInvariant() switch
        {
            "TRIALING" => "Trialing",
            "ACTIVE" => "Active",
            "PAST_DUE" => "PastDue",
            "UNPAID" => "Unpaid",
            "PAUSED" => "Paused",
            "CANCELED" => "Canceled",
            "INCOMPLETE" => "Incomplete",
            "INCOMPLETE_EXPIRED" => "IncompleteExpired",
            _ => "Incomplete",
        };

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
