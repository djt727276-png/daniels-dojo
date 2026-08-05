using DanielsDojo.Domain.Commerce;
using DanielsDojo.Infrastructure.Persistence;

namespace DanielsDojo.IntegrationTests.Commerce;

/// <summary>An offer and the active price a purchase was actually made at.</summary>
internal sealed record OfferPrice(Guid OfferId, Guid PriceId);

/// <summary>
/// Builds the payment rows an entitlement has to point at.
/// </summary>
/// <remarks>
/// The schema refuses a purchase-sourced entitlement with no order line and a
/// subscription-sourced one with no subscription, so a test cannot fabricate access without
/// also fabricating the payment it came from. That is the point: it keeps the suites honest
/// about the rule the product depends on — access is a consequence of provider state, never
/// something written on its own.
/// </remarks>
internal static class CommerceFactory
{
    /// <summary>Adds an active membership offer and its monthly price.</summary>
    public static OfferPrice MembershipOffer(
        DanielsDojoDbContext context,
        string code,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(context);

        var offer = new Offer
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = "Membership",
            Description = "Monthly membership.",
            Kind = OfferKind.Membership,
            Status = CommerceStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        var price = new Price
        {
            Id = Guid.NewGuid(),
            OfferId = offer.Id,
            AmountMinor = 999,
            Currency = "USD",
            BillingInterval = BillingInterval.Month,
            BillingIntervalCount = 1,
            Status = CommerceStatus.Active,
            EffectiveFromUtc = now.AddDays(-30),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.Offers.Add(offer);
        context.Prices.Add(price);

        return new OfferPrice(offer.Id, price.Id);
    }

    /// <summary>Adds an active lifetime offer for one course and its one-time price.</summary>
    public static OfferPrice LifetimeOffer(
        DanielsDojoDbContext context,
        Guid courseId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(context);

        var offer = new Offer
        {
            Id = Guid.NewGuid(),
            Code = $"lifetime-{courseId:N}",
            Name = "Lifetime access",
            Description = "One-time purchase of a single course.",
            Kind = OfferKind.CourseLifetime,
            CourseId = courseId,
            Status = CommerceStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        var price = new Price
        {
            Id = Guid.NewGuid(),
            OfferId = offer.Id,
            AmountMinor = 1999,
            Currency = "USD",
            BillingInterval = BillingInterval.OneTime,
            BillingIntervalCount = 1,
            Status = CommerceStatus.Active,
            EffectiveFromUtc = now.AddDays(-30),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.Offers.Add(offer);
        context.Prices.Add(price);

        return new OfferPrice(offer.Id, price.Id);
    }

    /// <summary>Adds a paid order for one course and returns the line an entitlement hangs off.</summary>
    public static Guid PaidOrderItem(
        DanielsDojoDbContext context,
        Guid userId,
        OfferPrice offer,
        Guid courseId,
        DateTimeOffset paidAtUtc)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(offer);

        const long amountMinor = 1999;
        string reference = Guid.NewGuid().ToString("N");

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = OrderStatus.Paid,
            Currency = "USD",
            SubtotalMinor = amountMinor,
            TaxMinor = 0,
            TotalMinor = amountMinor,
            StripeCheckoutSessionId = $"cs_test_{reference}",
            StripePaymentIntentId = $"pi_test_{reference}",
            PaidAtUtc = paidAtUtc,
            CreatedAtUtc = paidAtUtc,
            UpdatedAtUtc = paidAtUtc,
        };

        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            OfferId = offer.OfferId,
            PriceId = offer.PriceId,
            CourseId = courseId,
            OfferName = "Lifetime access",
            UnitAmountMinor = amountMinor,
            Currency = "USD",
            Quantity = 1,
            LineTotalMinor = amountMinor,
        };

        context.Orders.Add(order);
        context.OrderItems.Add(item);

        return item.Id;
    }

    /// <summary>Adds a membership subscription and returns its identifier.</summary>
    /// <param name="currentPeriodEndUtc">
    /// The paid-through boundary. A cancellation moves this rather than ending the row outright,
    /// which is what lets a cancelled member keep the period they already paid for.
    /// </param>
    public static Guid Subscription(
        DanielsDojoDbContext context,
        Guid userId,
        OfferPrice offer,
        DateTimeOffset currentPeriodStartUtc,
        DateTimeOffset currentPeriodEndUtc,
        SubscriptionStatus status,
        bool cancelAtPeriodEnd = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(offer);

        var subscription = new Domain.Commerce.Subscription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OfferId = offer.OfferId,
            PriceId = offer.PriceId,
            StripeSubscriptionId = $"sub_test_{Guid.NewGuid():N}",
            Status = status,
            CurrentPeriodStartUtc = currentPeriodStartUtc,
            CurrentPeriodEndUtc = currentPeriodEndUtc,
            CancelAtPeriodEnd = cancelAtPeriodEnd,
            CanceledAtUtc = cancelAtPeriodEnd ? currentPeriodStartUtc : null,
            CreatedAtUtc = currentPeriodStartUtc,
            UpdatedAtUtc = currentPeriodStartUtc,
        };

        context.Subscriptions.Add(subscription);

        return subscription.Id;
    }
}
