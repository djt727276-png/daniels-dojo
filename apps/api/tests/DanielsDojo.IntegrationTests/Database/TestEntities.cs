using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Domain.Learning;

namespace DanielsDojo.IntegrationTests.Database;

/// <summary>
/// Builds valid entities so each test can change exactly the one field it is asserting on.
/// Everything defaults to a state the database accepts.
/// </summary>
internal static class TestEntities
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    public static User User(string? subject = null, string? email = null) => new()
    {
        Id = Guid.NewGuid(),
        IdentityProvider = "TestProvider",
        ExternalIssuer = "https://issuer.test",
        ExternalSubjectId = subject ?? Guid.NewGuid().ToString("N"),
        Email = email ?? $"{Guid.NewGuid():N}@example.test",
        NormalizedEmail = (email ?? $"{Guid.NewGuid():N}@example.test").ToUpperInvariant(),
        DisplayName = "Test User",
        EmailVerified = true,
        Status = UserStatus.Active,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static Role Role(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        Description = "Test role.",
        IsAssignable = true,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static Course Course(string? slug = null) => new()
    {
        Id = Guid.NewGuid(),
        Slug = slug ?? $"course-{Guid.NewGuid():N}",
        Title = "Test Course",
        Summary = "Summary.",
        Description = "Description.",
        Level = CourseLevel.AllLevels,
        Status = PublicationStatus.Draft,
        IncludedInMembership = true,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static CourseSection Section(Guid courseId, int sortOrder) => new()
    {
        Id = Guid.NewGuid(),
        CourseId = courseId,
        Title = "Test Section",
        SortOrder = sortOrder,
        Status = PublicationStatus.Draft,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static Lesson Lesson(Guid courseId, Guid sectionId, string? slug = null, int sortOrder = 1) => new()
    {
        Id = Guid.NewGuid(),
        CourseId = courseId,
        CourseSectionId = sectionId,
        Slug = slug ?? $"lesson-{Guid.NewGuid():N}",
        Title = "Test Lesson",
        LessonType = LessonType.Article,
        BodyMarkdown = "# Test",
        SortOrder = sortOrder,
        IsPreview = false,
        Status = PublicationStatus.Draft,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static LessonVideo LessonVideo(Guid lessonId, string? assetId = null, string? playbackId = null) => new()
    {
        Id = Guid.NewGuid(),
        LessonId = lessonId,
        MuxAssetId = assetId,
        MuxPlaybackId = playbackId,
        Status = LessonVideoStatus.Requested,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static LessonResource LessonResource(Guid lessonId, string? blobObjectName = null) => new()
    {
        Id = Guid.NewGuid(),
        LessonId = lessonId,
        DisplayName = "Handout",
        BlobObjectName = blobObjectName,
        MediaType = "application/pdf",
        SizeBytes = 1024,
        SortOrder = 1,
        Status = PublicationStatus.Draft,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static Offer MembershipOffer(string? code = null) => new()
    {
        Id = Guid.NewGuid(),
        Code = code ?? $"offer-{Guid.NewGuid():N}",
        Name = "Test Membership",
        Description = "Description.",
        Kind = OfferKind.Membership,
        CourseId = null,
        Status = CommerceStatus.Active,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static Offer CourseOffer(Guid courseId, string? code = null) => new()
    {
        Id = Guid.NewGuid(),
        Code = code ?? $"offer-{Guid.NewGuid():N}",
        Name = "Test Course Offer",
        Description = "Description.",
        Kind = OfferKind.CourseLifetime,
        CourseId = courseId,
        Status = CommerceStatus.Active,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static Price Price(Guid offerId, long amountMinor = 999, string currency = "USD") => new()
    {
        Id = Guid.NewGuid(),
        OfferId = offerId,
        AmountMinor = amountMinor,
        Currency = currency,
        BillingInterval = BillingInterval.Month,
        BillingIntervalCount = 1,
        Status = CommerceStatus.Active,
        EffectiveFromUtc = Now,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static Order Order(Guid userId, long subtotal = 1999, long tax = 0) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Status = OrderStatus.Pending,
        Currency = "USD",
        SubtotalMinor = subtotal,
        TaxMinor = tax,
        TotalMinor = subtotal + tax,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static OrderItem OrderItem(Guid orderId, Guid offerId, Guid priceId, Guid courseId) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        OfferId = offerId,
        PriceId = priceId,
        CourseId = courseId,
        OfferName = "Test Course Offer",
        UnitAmountMinor = 1999,
        Currency = "USD",
        Quantity = 1,
        LineTotalMinor = 1999,
    };

    public static Subscription Subscription(Guid userId, Guid offerId, Guid priceId, string? externalId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        OfferId = offerId,
        PriceId = priceId,
        StripeSubscriptionId = externalId ?? $"sub_{Guid.NewGuid():N}",
        Status = SubscriptionStatus.Active,
        CurrentPeriodStartUtc = Now,
        CurrentPeriodEndUtc = Now.AddMonths(1),
        CancelAtPeriodEnd = false,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static Entitlement MembershipEntitlement(Guid userId, Guid subscriptionId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Scope = EntitlementScope.AllMembershipCourses,
        Source = EntitlementSource.Subscription,
        CourseId = null,
        SubscriptionId = subscriptionId,
        OrderItemId = null,
        Status = EntitlementStatus.Active,
        StartsAtUtc = Now,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static Entitlement PurchaseEntitlement(Guid userId, Guid courseId, Guid orderItemId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Scope = EntitlementScope.Course,
        Source = EntitlementSource.Purchase,
        CourseId = courseId,
        SubscriptionId = null,
        OrderItemId = orderItemId,
        Status = EntitlementStatus.Active,
        StartsAtUtc = Now,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static WebhookEvent WebhookEvent(string? externalId = null) => new()
    {
        Id = Guid.NewGuid(),
        Provider = "Stripe",
        ExternalEventId = externalId ?? $"evt_{Guid.NewGuid():N}",
        EventType = "invoice.paid",
        Status = WebhookEventStatus.Received,
        AttemptCount = 0,
        ReceivedAtUtc = Now,
        PayloadSha256 = new string('a', 64),
    };

    public static Refund OrderRefund(Guid orderId, string? refundId = null) => new()
    {
        Id = Guid.NewGuid(),
        StripeRefundId = refundId ?? $"re_{Guid.NewGuid():N}",
        OrderId = orderId,
        SubscriptionId = null,
        StripePaymentIntentId = $"pi_{Guid.NewGuid():N}",
        AmountMinor = 999,
        Currency = "USD",
        Status = RefundStatus.Succeeded,
        Reason = "requested_by_customer",
        IsFullRefund = false,
        RequiresAccessReview = true,
        OccurredAtUtc = Now,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static PaymentDispute OrderDispute(Guid orderId, string? disputeId = null) => new()
    {
        Id = Guid.NewGuid(),
        StripeDisputeId = disputeId ?? $"dp_{Guid.NewGuid():N}",
        StripeChargeId = $"ch_{Guid.NewGuid():N}",
        OrderId = orderId,
        SubscriptionId = null,
        AmountMinor = 1999,
        Currency = "USD",
        Status = PaymentDisputeStatus.NeedsResponse,
        Reason = "fraudulent",
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static Enrollment Enrollment(Guid userId, Guid courseId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        CourseId = courseId,
        EnrolledAtUtc = Now,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static LessonProgress LessonProgress(Guid userId, Guid lessonId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        LessonId = lessonId,
        StartedAtUtc = Now,
        LastPositionSeconds = 0,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };
}
