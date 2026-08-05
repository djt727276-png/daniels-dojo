using System.Globalization;
using DanielsDojo.Application.Commerce;
using DanielsDojo.Application.Common;
using DanielsDojo.Application.Community;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Domain.Community;
using DanielsDojo.Domain.Media;
using DanielsDojo.Infrastructure.Auditing;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DanielsDojo.Infrastructure.Commerce;

/// <summary>
/// Customer purchasing.
/// </summary>
/// <remarks>
/// <para>
/// A checkout produces a pending local order (or subscription intent) before the customer is
/// redirected, so every provider session traces to a local row from birth. Confirmation then
/// reads the session back from the provider — the redirect and the webhook both land here,
/// idempotently, and whichever arrives first does the work.
/// </para>
/// <para>
/// Entitlements are written in the same transaction as the order state that justifies them,
/// which is what makes "access is a consequence of provider state" literally true in the
/// database: there is no moment where one exists without the other.
/// </para>
/// </remarks>
internal sealed class CheckoutService : ICheckoutService
{
    private readonly DanielsDojoDbContext context;
    private readonly IPaymentProvider payments;
    private readonly TimeProvider timeProvider;
    private readonly IRealtimeNotifier realtime;
    private readonly AuditTrail audit;

    public CheckoutService(
        DanielsDojoDbContext context,
        IPaymentProvider payments,
        IOperationContext operationContext,
        TimeProvider timeProvider,
        IRealtimeNotifier realtime)
    {
        this.context = context;
        this.payments = payments;
        this.timeProvider = timeProvider;
        this.realtime = realtime;

        audit = new AuditTrail(context, operationContext, timeProvider);
    }

    public async Task<OperationResult<CheckoutStarted>> StartCheckoutAsync(
        Guid userId,
        StartCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (payments.Mode == ProviderMode.Disabled)
        {
            return OperationResult.Conflict(
                CommerceErrorCodes.ProviderDisabled,
                "Purchasing is switched off in this environment.")
                .ToFailure<CheckoutStarted>();
        }

        // Operator kill switch, checked at the door. A missing row means the default: on.
        // Access already granted is untouched — only new checkouts are refused.
        if (await context.FeatureFlags
                .AsNoTracking()
                .Where(flag => flag.Key == "checkout")
                .Select(flag => (bool?)flag.Enabled)
                .FirstOrDefaultAsync(cancellationToken) == false)
        {
            return OperationResult.Conflict(
                CommerceErrorCodes.ProviderDisabled,
                "Purchasing is paused right now. Please try again later.")
                .ToFailure<CheckoutStarted>();
        }

        Offer? offer = await context.Offers
            .AsNoTracking()
            .Include(candidate => candidate.Prices)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == request.OfferId
                    && candidate.Status == CommerceStatus.Active,
                cancellationToken);

        Price? price = offer?.Prices
            .Where(candidate => candidate.Status == CommerceStatus.Active)
            .OrderByDescending(candidate => candidate.EffectiveFromUtc)
            .FirstOrDefault();

        if (offer is null || price is null)
        {
            return OperationResult.Conflict(
                CommerceErrorCodes.OfferUnavailable,
                "This offer is not available for purchase right now.")
                .ToFailure<CheckoutStarted>();
        }

        if (await AlreadyOwnedAsync(userId, offer, cancellationToken))
        {
            return OperationResult.Conflict(
                CommerceErrorCodes.AlreadyOwned,
                "You already have access to what this offer grants.")
                .ToFailure<CheckoutStarted>();
        }

        var user = await context.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new { candidate.Email, candidate.DisplayName })
            .SingleAsync(cancellationToken);

        string customerId = await payments.EnsureCustomerAsync(
            userId, user.Email, user.DisplayName, cancellationToken);

        await EnsureCustomerLinkAsync(userId, customerId, cancellationToken);

        bool isRecurring = offer.Kind == OfferKind.Membership;

        (_, string providerPriceId) = await payments.EnsurePriceAsync(
            offer.Code,
            offer.Name,
            price.Id,
            price.AmountMinor,
            price.Currency,
            isRecurring,
            cancellationToken);

        DateTimeOffset now = timeProvider.GetUtcNow();

        // The local order exists before the redirect, so a session can never come back that
        // this database has no record of asking for. Its identifier rides along as the client
        // reference and is what a webhook uses to find its way home.
        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Status = OrderStatus.Pending,
            Currency = price.Currency,
            SubtotalMinor = price.AmountMinor,
            TaxMinor = 0,
            TotalMinor = price.AmountMinor,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        CheckoutTicket ticket = await payments.CreateCheckoutAsync(
            customerId,
            providerPriceId,
            isRecurring,
            order.Id.ToString("D"),
            cancellationToken);

        order.StripeCheckoutSessionId = ticket.SessionId;

        context.Orders.Add(order);
        context.OrderItems.Add(new OrderItem
        {
            Id = Guid.CreateVersion7(),
            OrderId = order.Id,
            OfferId = offer.Id,
            PriceId = price.Id,

            // A membership order line records the offer without a course; a lifetime line
            // names the course it buys.
            CourseId = offer.CourseId,
            OfferName = offer.Name,
            UnitAmountMinor = price.AmountMinor,
            Currency = price.Currency,
            Quantity = 1,
            LineTotalMinor = price.AmountMinor,
        });

        audit.Append(
            "Commerce.Checkout.Started",
            nameof(Order),
            order.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["offerId"] = offer.Id.ToString("D"),
                ["offerKind"] = offer.Kind.ToString(),
                ["amountMinor"] = price.AmountMinor.ToString(CultureInfo.InvariantCulture),
                ["providerMode"] = payments.Mode.ToString(),
            });

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.FromValue(new CheckoutStarted(ticket.Url));
    }

    public async Task<OperationResult<CheckoutConfirmed>> ConfirmCheckoutAsync(
        Guid userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        Order? order = await context.Orders
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(
                candidate => candidate.StripeCheckoutSessionId == sessionId
                    && candidate.UserId == userId,
                cancellationToken);

        if (order is null)
        {
            // Not found rather than forbidden: confirming somebody else's session should not
            // reveal that it exists.
            return OperationResult.NotFound().ToFailure<CheckoutConfirmed>();
        }

        if (order.Status == OrderStatus.Paid)
        {
            // The webhook got here first. Confirmation is idempotent, not an error.
            return OperationResult.FromValue(new CheckoutConfirmed(true, EntitlementGranted: true));
        }

        // The browser said "success". Ask the provider.
        CheckoutState? state = await payments.GetCheckoutAsync(sessionId, cancellationToken);

        if (state is null)
        {
            return OperationResult.Conflict(
                CommerceErrorCodes.CheckoutNotFound,
                "The payment provider does not recognise this checkout.")
                .ToFailure<CheckoutConfirmed>();
        }

        if (!state.IsPaid)
        {
            return OperationResult.FromValue(new CheckoutConfirmed(false, EntitlementGranted: false));
        }

        await SettleOrderAsync(order, state, cancellationToken);

        return OperationResult.FromValue(new CheckoutConfirmed(true, EntitlementGranted: true));
    }

    public async Task<OperationResult<PortalStarted>> StartPortalAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (payments.Mode == ProviderMode.Disabled)
        {
            return OperationResult.Conflict(
                CommerceErrorCodes.ProviderDisabled,
                "Billing management is switched off in this environment.")
                .ToFailure<PortalStarted>();
        }

        StripeCustomer? link = await context.StripeCustomers
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

        if (link is null)
        {
            return OperationResult.Conflict(
                CommerceErrorCodes.NoBillingProfile,
                "There is nothing to manage yet — you have not made a purchase.")
                .ToFailure<PortalStarted>();
        }

        PortalTicket ticket = await payments.CreatePortalAsync(link.StripeCustomerId, cancellationToken);

        return OperationResult.FromValue(new PortalStarted(ticket.Url));
    }

    public async Task<OperationResult<BillingOverview>> GetBillingAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        Domain.Commerce.Subscription? membership = await context.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId
                && (subscription.Status == SubscriptionStatus.Active
                    || subscription.Status == SubscriptionStatus.Trialing
                    || subscription.Status == SubscriptionStatus.PastDue))
            .OrderByDescending(subscription => subscription.CurrentPeriodEndUtc)
            .FirstOrDefaultAsync(cancellationToken);

        List<OrderSummary> orders = await context.Orders
            .AsNoTracking()
            .Where(order => order.UserId == userId)
            .OrderByDescending(order => order.CreatedAtUtc)
            .Take(50)
            .Select(order => new OrderSummary(
                order.Id,
                order.Status.ToString(),
                order.TotalMinor,
                order.Currency,
                order.Items.Select(item => item.OfferName).FirstOrDefault() ?? "Purchase",
                order.PaidAtUtc))
            .ToListAsync(cancellationToken);

        return OperationResult.FromValue(new BillingOverview(
            membership is null
                ? null
                : new MembershipSummary(
                    membership.Status.ToString(),
                    membership.CurrentPeriodEndUtc,
                    membership.CancelAtPeriodEnd),
            orders));
    }

    // ------------------------------------------------------------------ settlement

    /// <summary>
    /// Records a paid order and writes the access it bought, in one transaction.
    /// </summary>
    /// <remarks>
    /// Everything recorded here comes from <paramref name="state"/> — what the provider
    /// reported — never from the request that triggered the confirmation. Reached identically
    /// from the browser-return path and the webhook path; the order status check on entry is
    /// what makes the second arrival a no-op.
    /// </remarks>
    internal async Task SettleOrderAsync(
        Order order,
        CheckoutState state,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        order.Status = OrderStatus.Paid;
        order.PaidAtUtc = now;
        order.StripePaymentIntentId = state.PaymentIntentId;
        order.UpdatedAtUtc = now;

        OrderItem line = order.Items.Single();

        Offer offer = await context.Offers
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == line.OfferId, cancellationToken);

        if (offer.Kind == OfferKind.Membership && state.SubscriptionId is { Length: > 0 } subscriptionId)
        {
            SubscriptionState? provider = await payments.GetSubscriptionAsync(
                subscriptionId, cancellationToken);

            var subscription = new Domain.Commerce.Subscription
            {
                Id = Guid.CreateVersion7(),
                UserId = order.UserId,
                OfferId = offer.Id,
                PriceId = line.PriceId,
                StripeSubscriptionId = subscriptionId,
                Status = Enum.Parse<SubscriptionStatus>(
                    StripeEventParser.MapSubscriptionStatus(provider?.Status ?? "active")),
                CurrentPeriodStartUtc = provider?.CurrentPeriodStart ?? now,
                CurrentPeriodEndUtc = provider?.CurrentPeriodEnd ?? now.AddMonths(1),
                CancelAtPeriodEnd = provider?.CancelAtPeriodEnd ?? false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            context.Subscriptions.Add(subscription);

            context.Entitlements.Add(new Entitlement
            {
                Id = Guid.CreateVersion7(),
                UserId = order.UserId,
                Scope = EntitlementScope.AllMembershipCourses,
                Source = EntitlementSource.Subscription,
                SubscriptionId = subscription.Id,
                Status = EntitlementStatus.Active,
                StartsAtUtc = now,

                // The paid-through boundary, maintained by subscription webhooks thereafter.
                EndsAtUtc = subscription.CurrentPeriodEndUtc,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }
        else
        {
            context.Entitlements.Add(new Entitlement
            {
                Id = Guid.CreateVersion7(),
                UserId = order.UserId,
                Scope = EntitlementScope.Course,
                Source = EntitlementSource.Purchase,
                CourseId = line.CourseId!.Value,
                OrderItemId = line.Id,
                Status = EntitlementStatus.Active,
                StartsAtUtc = now,
                EndsAtUtc = null,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        audit.Append(
            "Commerce.Order.Paid",
            nameof(Order),
            order.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["offerKind"] = offer.Kind.ToString(),
                ["amountMinor"] = (state.AmountTotalMinor ?? order.TotalMinor)
                    .ToString(CultureInfo.InvariantCulture),
                ["providerMode"] = payments.Mode.ToString(),
            });

        // Written in the same transaction as the order and entitlement it announces, and
        // only on the single Pending→Paid transition, so a webhook/redirect race cannot
        // duplicate it.
        context.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(),
            RecipientUserId = order.UserId,
            ActorUserId = null,
            Kind = NotificationKind.PurchaseCompleted,
            TargetType = nameof(Order),
            TargetId = order.Id,
            CreatedAtUtc = now,
        });

        await context.SaveChangesAsync(cancellationToken);

        // Persisted first, rung after: the doorbell only tells the member to go and fetch.
        await realtime.UnreadChangedAsync(order.UserId, cancellationToken);
    }

    /// <summary>
    /// Whether the customer already holds what this offer grants — an active membership for a
    /// membership offer, or an active lifetime entitlement for that course.
    /// </summary>
    private async Task<bool> AlreadyOwnedAsync(
        Guid userId,
        Offer offer,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        return offer.Kind == OfferKind.Membership
            ? await context.Entitlements.AsNoTracking().AnyAsync(
                grant => grant.UserId == userId
                    && grant.Scope == EntitlementScope.AllMembershipCourses
                    && grant.Status == EntitlementStatus.Active
                    && (grant.EndsAtUtc == null || grant.EndsAtUtc > now),
                cancellationToken)
            : await context.Entitlements.AsNoTracking().AnyAsync(
                grant => grant.UserId == userId
                    && grant.Scope == EntitlementScope.Course
                    && grant.CourseId == offer.CourseId
                    && grant.Source == EntitlementSource.Purchase
                    && grant.Status == EntitlementStatus.Active,
                cancellationToken);
    }

    private async Task EnsureCustomerLinkAsync(
        Guid userId,
        string customerId,
        CancellationToken cancellationToken)
    {
        bool exists = await context.StripeCustomers
            .AnyAsync(link => link.UserId == userId, cancellationToken);

        if (exists)
        {
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        context.StripeCustomers.Add(new StripeCustomer
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            StripeCustomerId = customerId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
    }
}
