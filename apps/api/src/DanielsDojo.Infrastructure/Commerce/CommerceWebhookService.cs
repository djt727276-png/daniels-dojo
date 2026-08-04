using System.Security.Cryptography;
using System.Text;
using DanielsDojo.Application.Commerce;
using DanielsDojo.Application.Common;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Infrastructure.Auditing;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DanielsDojo.Infrastructure.Commerce;

/// <summary>
/// Applies inbound payment notifications.
/// </summary>
/// <remarks>
/// <para>
/// The same discipline as the media webhook: signed or nothing, applied at most once, and a
/// rejected delivery writes no row and logs no payload. Payment events additionally always
/// re-read the object they describe from the provider — the event is a doorbell, and the
/// truth is fetched fresh, so a forged or mangled body could at worst trigger a lookup.
/// </para>
/// <para>
/// Subscription lifecycle events keep the entitlement's paid-through boundary in step with
/// the provider, which is how a cancelled membership keeps working until the period ends and
/// how a lapsed one stops.
/// </para>
/// </remarks>
internal sealed class CommerceWebhookService : ICommerceWebhookService
{
    private const string WebhookProvider = "Stripe";

    private readonly DanielsDojoDbContext context;
    private readonly IPaymentProvider payments;
    private readonly CheckoutService settlement;
    private readonly TimeProvider timeProvider;
    private readonly AuditTrail audit;

    public CommerceWebhookService(
        DanielsDojoDbContext context,
        IPaymentProvider payments,
        ICheckoutService checkout,
        IOperationContext operationContext,
        TimeProvider timeProvider)
    {
        this.context = context;
        this.payments = payments;
        this.timeProvider = timeProvider;

        // Settlement lives in one place; the webhook path and the browser-return path must
        // reach identical conclusions from identical provider state.
        settlement = (CheckoutService)checkout;

        audit = new AuditTrail(context, operationContext, timeProvider);
    }

    public async Task<bool> HandlePaymentEventAsync(
        string payload,
        string? signatureHeader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        DateTimeOffset now = timeProvider.GetUtcNow();

        if (payments.VerifyEvent(payload, signatureHeader, now) is not { } notification)
        {
            return false;
        }

        bool alreadySeen = await context.WebhookEvents.AnyAsync(
            seen => seen.Provider == WebhookProvider && seen.ExternalEventId == notification.EventId,
            cancellationToken);

        if (alreadySeen)
        {
            return true;
        }

        var record = new WebhookEvent
        {
            Id = Guid.CreateVersion7(),
            Provider = WebhookProvider,
            ExternalEventId = notification.EventId,
            EventType = notification.EventType,
            Status = WebhookEventStatus.Received,
            AttemptCount = 1,
            ReceivedAtUtc = now,
            PayloadSha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant(),
        };

        context.WebhookEvents.Add(record);

        bool applied = notification.EventType switch
        {
            "checkout.session.completed" =>
                await ApplyCheckoutCompletedAsync(notification, cancellationToken),

            "customer.subscription.updated" or "customer.subscription.deleted" =>
                await ApplySubscriptionChangedAsync(notification, cancellationToken),

            "refund.created" or "refund.updated" or "charge.refunded" =>
                await ApplyRefundAsync(notification, now, cancellationToken),

            "charge.dispute.created" or "charge.dispute.updated" or "charge.dispute.closed" =>
                await ApplyDisputeAsync(notification, now, cancellationToken),

            _ => false,
        };

        record.Status = applied ? WebhookEventStatus.Processed : WebhookEventStatus.Ignored;
        record.ProcessedAtUtc = now;

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    // ------------------------------------------------------------------ handlers

    private async Task<bool> ApplyCheckoutCompletedAsync(
        PaymentProviderEvent notification,
        CancellationToken cancellationToken)
    {
        if (notification.SessionId is not { Length: > 0 } sessionId)
        {
            return false;
        }

        Order? order = await context.Orders
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(
                candidate => candidate.StripeCheckoutSessionId == sessionId,
                cancellationToken);

        if (order is null || order.Status == OrderStatus.Paid)
        {
            // Unknown session, or the browser-return path settled it first. Both are fine.
            return false;
        }

        // The event is the doorbell; the session itself is the truth.
        CheckoutState? state = await payments.GetCheckoutAsync(sessionId, cancellationToken);

        if (state is not { IsPaid: true })
        {
            return false;
        }

        await settlement.SettleOrderAsync(order, state, cancellationToken);

        return true;
    }

    private async Task<bool> ApplySubscriptionChangedAsync(
        PaymentProviderEvent notification,
        CancellationToken cancellationToken)
    {
        if (notification.SubscriptionId is not { Length: > 0 } subscriptionId)
        {
            return false;
        }

        Domain.Commerce.Subscription? subscription = await context.Subscriptions
            .FirstOrDefaultAsync(
                candidate => candidate.StripeSubscriptionId == subscriptionId,
                cancellationToken);

        if (subscription is null)
        {
            return false;
        }

        SubscriptionState? state = await payments.GetSubscriptionAsync(subscriptionId, cancellationToken);

        if (state is null)
        {
            return false;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        subscription.Status = Enum.Parse<SubscriptionStatus>(
            StripeEventParser.MapSubscriptionStatus(state.Status));
        subscription.CurrentPeriodStartUtc = state.CurrentPeriodStart;
        subscription.CurrentPeriodEndUtc = state.CurrentPeriodEnd;
        subscription.CancelAtPeriodEnd = state.CancelAtPeriodEnd;
        subscription.CanceledAtUtc = state.CanceledAt;
        subscription.EndedAtUtc = state.EndedAt;
        subscription.UpdatedAtUtc = now;

        Entitlement? entitlement = await context.Entitlements
            .FirstOrDefaultAsync(
                candidate => candidate.SubscriptionId == subscription.Id
                    && candidate.Status == EntitlementStatus.Active,
                cancellationToken);

        if (entitlement is not null)
        {
            // Cancellation moves the boundary; it does not revoke. The member keeps what they
            // paid for until it runs out, and the access evaluator enforces the boundary.
            // Clamped to the grant's own start: a provider reporting an end before the local
            // start would otherwise produce a grant that ended before it began, which the
            // schema rightly refuses.
            DateTimeOffset boundary = state.EndedAt ?? state.CurrentPeriodEnd;

            entitlement.EndsAtUtc = boundary < entitlement.StartsAtUtc
                ? entitlement.StartsAtUtc
                : boundary;
            entitlement.UpdatedAtUtc = now;

            bool lapsed = subscription.Status
                is SubscriptionStatus.Canceled
                or SubscriptionStatus.IncompleteExpired
                or SubscriptionStatus.Unpaid;

            if (lapsed && entitlement.EndsAtUtc <= now)
            {
                entitlement.Status = EntitlementStatus.Expired;
            }
        }

        audit.Append(
            "Commerce.Subscription.Synced",
            nameof(Domain.Commerce.Subscription),
            subscription.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["status"] = subscription.Status.ToString(),
                ["cancelAtPeriodEnd"] = subscription.CancelAtPeriodEnd ? "true" : "false",
            });

        return true;
    }

    private async Task<bool> ApplyRefundAsync(
        PaymentProviderEvent notification,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (notification.PaymentIntentId is not { Length: > 0 } paymentIntentId)
        {
            return false;
        }

        Order? order = await context.Orders
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(
                candidate => candidate.StripePaymentIntentId == paymentIntentId,
                cancellationToken);

        if (order is null)
        {
            return false;
        }

        string refundId = notification.RefundId ?? $"refund-{notification.EventId}";

        bool recorded = await context.Refunds.AnyAsync(
            refund => refund.StripeRefundId == refundId, cancellationToken);

        if (recorded)
        {
            return false;
        }

        context.Refunds.Add(new Refund
        {
            Id = Guid.CreateVersion7(),
            StripeRefundId = refundId,
            OrderId = order.Id,
            StripePaymentIntentId = paymentIntentId,
            AmountMinor = order.TotalMinor,
            Currency = order.Currency,
            Status = RefundStatus.Succeeded,
            Reason = "Reported by the payment provider.",
            IsFullRefund = true,

            // Access is revoked by a person after review, never silently by a webhook — a
            // partial refund or goodwill credit must not strip a customer's course.
            RequiresAccessReview = true,
            OccurredAtUtc = notification.OccurredAtUtc == DateTimeOffset.MinValue
                ? now
                : notification.OccurredAtUtc,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        order.Status = OrderStatus.Refunded;
        order.UpdatedAtUtc = now;

        audit.Append(
            "Commerce.Refund.Recorded",
            nameof(Order),
            order.Id,
            reason: "Provider reported a refund; entitlement review required.");

        return true;
    }

    private async Task<bool> ApplyDisputeAsync(
        PaymentProviderEvent notification,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (notification.PaymentIntentId is not { Length: > 0 } paymentIntentId
            || notification.DisputeId is not { Length: > 0 } disputeId)
        {
            return false;
        }

        Order? order = await context.Orders.FirstOrDefaultAsync(
            candidate => candidate.StripePaymentIntentId == paymentIntentId,
            cancellationToken);

        PaymentDispute? existing = await context.PaymentDisputes.FirstOrDefaultAsync(
            candidate => candidate.StripeDisputeId == disputeId,
            cancellationToken);

        if (existing is not null)
        {
            existing.UpdatedAtUtc = now;
            return true;
        }

        context.PaymentDisputes.Add(new PaymentDispute
        {
            Id = Guid.CreateVersion7(),
            StripeDisputeId = disputeId,
            StripeChargeId = paymentIntentId,
            OrderId = order?.Id,
            AmountMinor = order?.TotalMinor ?? 0,
            Currency = order?.Currency ?? "USD",
            Status = PaymentDisputeStatus.NeedsResponse,
            Reason = "Reported by the payment provider.",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        if (order is not null)
        {
            order.Status = OrderStatus.Disputed;
            order.UpdatedAtUtc = now;

            audit.Append(
                "Commerce.Dispute.Recorded",
                nameof(Order),
                order.Id,
                reason: "Provider reported a dispute.");
        }

        return true;
    }
}
