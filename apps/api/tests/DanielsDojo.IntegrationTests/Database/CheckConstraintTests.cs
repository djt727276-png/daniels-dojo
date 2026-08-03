using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Domain.Learning;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Database;

/// <summary>
/// Proves the business rules that are expressed as SQL check constraints reject invalid data
/// at the database, not merely in application code.
/// </summary>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class CheckConstraintTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task MembershipOfferWithCourse_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Course course = TestEntities.Course();
        context.Courses.Add(course);
        await context.SaveChangesAsync();

        Offer invalid = TestEntities.MembershipOffer();
        invalid.CourseId = course.Id;
        context.Offers.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_Offers_MembershipForbidsCourse");
    }

    [Fact]
    public async Task CourseLifetimeOfferWithoutCourse_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Offer invalid = TestEntities.MembershipOffer();
        invalid.Kind = OfferKind.CourseLifetime;
        invalid.CourseId = null;
        context.Offers.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_Offers_CourseLifetimeRequiresCourse");
    }

    [Fact]
    public async Task ZeroOrNegativePriceAmount_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Offer offer = TestEntities.MembershipOffer();
        context.Offers.Add(offer);
        await context.SaveChangesAsync();

        context.Prices.Add(TestEntities.Price(offer.Id, amountMinor: 0));
        await AssertCheckViolationAsync(context, "CK_Prices_AmountMinor_Positive");
    }

    [Fact]
    public async Task LowercaseCurrency_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Offer offer = TestEntities.MembershipOffer();
        context.Offers.Add(offer);
        await context.SaveChangesAsync();

        context.Prices.Add(TestEntities.Price(offer.Id, currency: "usd"));
        await AssertCheckViolationAsync(context, "CK_Prices_Currency_Uppercase");
    }

    [Fact]
    public async Task BillingIntervalCountOtherThanOne_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Offer offer = TestEntities.MembershipOffer();
        context.Offers.Add(offer);
        await context.SaveChangesAsync();

        Price invalid = TestEntities.Price(offer.Id);
        invalid.BillingIntervalCount = 3;
        context.Prices.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_Prices_BillingIntervalCount_One");
    }

    [Fact]
    public async Task PriceRetiredBeforeEffective_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Offer offer = TestEntities.MembershipOffer();
        context.Offers.Add(offer);
        await context.SaveChangesAsync();

        Price invalid = TestEntities.Price(offer.Id);
        invalid.EffectiveFromUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        invalid.RetiredAtUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        context.Prices.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_Prices_RetiredAfterEffective");
    }

    [Fact]
    public async Task OrderTotalThatDoesNotReconcile_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Order invalid = TestEntities.Order(graph.UserId, subtotal: 1000, tax: 100);
        invalid.TotalMinor = 1200; // Should be 1100.
        context.Orders.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_Orders_Total_Reconciles");
    }

    [Fact]
    public async Task NegativeOrderAmount_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Order invalid = TestEntities.Order(graph.UserId);
        invalid.SubtotalMinor = -100;
        invalid.TaxMinor = 0;
        invalid.TotalMinor = -100;
        context.Orders.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_Orders_Amounts_NonNegative");
    }

    [Fact]
    public async Task OrderItemQuantityAboveOne_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Order order = TestEntities.Order(graph.UserId);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        OrderItem invalid = TestEntities.OrderItem(
            order.Id, graph.CourseOfferId, graph.PriceId, graph.CourseId);
        invalid.Quantity = 2;
        invalid.LineTotalMinor = invalid.UnitAmountMinor * 2;
        context.OrderItems.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_OrderItems_Quantity_One");
    }

    [Fact]
    public async Task CourseScopedEntitlementWithoutCourse_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Order order = TestEntities.Order(graph.UserId);
        OrderItem item = TestEntities.OrderItem(
            order.Id, graph.CourseOfferId, graph.PriceId, graph.CourseId);
        context.Orders.Add(order);
        context.OrderItems.Add(item);
        await context.SaveChangesAsync();

        Entitlement invalid = TestEntities.PurchaseEntitlement(graph.UserId, graph.CourseId, item.Id);
        invalid.CourseId = null;
        context.Entitlements.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_Entitlements_CourseScopeRequiresCourse");
    }

    [Fact]
    public async Task MembershipScopedEntitlementWithCourse_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Subscription subscription =
            TestEntities.Subscription(graph.UserId, graph.MembershipOfferId, graph.PriceId);
        context.Subscriptions.Add(subscription);
        await context.SaveChangesAsync();

        Entitlement invalid = TestEntities.MembershipEntitlement(graph.UserId, subscription.Id);
        invalid.CourseId = graph.CourseId;
        context.Entitlements.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_Entitlements_MembershipScopeForbidsCourse");
    }

    [Fact]
    public async Task SubscriptionSourcedEntitlementCarryingAnOrderItem_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Subscription subscription =
            TestEntities.Subscription(graph.UserId, graph.MembershipOfferId, graph.PriceId);
        Order order = TestEntities.Order(graph.UserId);
        OrderItem item = TestEntities.OrderItem(
            order.Id, graph.CourseOfferId, graph.PriceId, graph.CourseId);

        context.Subscriptions.Add(subscription);
        context.Orders.Add(order);
        context.OrderItems.Add(item);
        await context.SaveChangesAsync();

        Entitlement invalid = TestEntities.MembershipEntitlement(graph.UserId, subscription.Id);
        invalid.OrderItemId = item.Id;
        context.Entitlements.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_Entitlements_SubscriptionSource");
    }

    [Fact]
    public async Task ManualEntitlementCarryingACommerceSource_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Subscription subscription =
            TestEntities.Subscription(graph.UserId, graph.MembershipOfferId, graph.PriceId);
        context.Subscriptions.Add(subscription);
        await context.SaveChangesAsync();

        Entitlement invalid = TestEntities.MembershipEntitlement(graph.UserId, subscription.Id);
        invalid.Source = EntitlementSource.Manual;
        context.Entitlements.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_Entitlements_ManualSource");
    }

    [Fact]
    public async Task ManualEntitlementWithNoCommerceSource_IsAccepted()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        User admin = TestEntities.User();
        context.Users.Add(admin);
        await context.SaveChangesAsync();

        context.Entitlements.Add(new Entitlement
        {
            Id = Guid.NewGuid(),
            UserId = graph.UserId,
            Scope = EntitlementScope.Course,
            Source = EntitlementSource.Manual,
            CourseId = graph.CourseId,
            Status = EntitlementStatus.Active,
            StartsAtUtc = DateTimeOffset.UtcNow,
            GrantedByUserId = admin.Id,
            GrantReason = "Complimentary access approved by support.",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        await context.SaveChangesAsync();

        Assert.Equal(1, await context.Entitlements.CountAsync());
    }

    [Fact]
    public async Task EntitlementEndingBeforeItStarts_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        context.Entitlements.Add(new Entitlement
        {
            Id = Guid.NewGuid(),
            UserId = graph.UserId,
            Scope = EntitlementScope.Course,
            Source = EntitlementSource.Manual,
            CourseId = graph.CourseId,
            Status = EntitlementStatus.Active,
            StartsAtUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            EndsAtUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        await AssertCheckViolationAsync(context, "CK_Entitlements_EndsAfterStarts");
    }

    [Fact]
    public async Task RefundWithBothSources_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Order order = TestEntities.Order(graph.UserId);
        Subscription subscription =
            TestEntities.Subscription(graph.UserId, graph.MembershipOfferId, graph.PriceId);
        context.Orders.Add(order);
        context.Subscriptions.Add(subscription);
        await context.SaveChangesAsync();

        Refund invalid = TestEntities.OrderRefund(order.Id);
        invalid.SubscriptionId = subscription.Id;
        context.Refunds.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_Refunds_ExactlyOneSource");
    }

    [Fact]
    public async Task RefundWithNoSource_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Order order = TestEntities.Order(graph.UserId);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        Refund invalid = TestEntities.OrderRefund(order.Id);
        invalid.OrderId = null;
        context.Refunds.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_Refunds_ExactlyOneSource");
    }

    [Fact]
    public async Task DisputeWithBothSources_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Order order = TestEntities.Order(graph.UserId);
        Subscription subscription =
            TestEntities.Subscription(graph.UserId, graph.MembershipOfferId, graph.PriceId);
        context.Orders.Add(order);
        context.Subscriptions.Add(subscription);
        await context.SaveChangesAsync();

        PaymentDispute invalid = TestEntities.OrderDispute(order.Id);
        invalid.SubscriptionId = subscription.Id;
        context.PaymentDisputes.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_PaymentDisputes_ExactlyOneSource");
    }

    [Fact]
    public async Task NegativeLessonProgressPosition_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        LearningGraph graph = await LearningGraph.CreateAsync(context);

        LessonProgress invalid = TestEntities.LessonProgress(graph.UserId, graph.LessonId);
        invalid.LastPositionSeconds = -1;
        context.LessonProgress.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_LessonProgress_LastPositionSeconds_NonNegative");
    }

    [Fact]
    public async Task CompletedLessonWithoutStart_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        LearningGraph graph = await LearningGraph.CreateAsync(context);

        LessonProgress invalid = TestEntities.LessonProgress(graph.UserId, graph.LessonId);
        invalid.StartedAtUtc = null;
        invalid.CompletedAtUtc = DateTimeOffset.UtcNow;
        context.LessonProgress.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_LessonProgress_CompletedRequiresStarted");
    }

    [Fact]
    public async Task PublishedLessonResourceWithoutBlob_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        LearningGraph graph = await LearningGraph.CreateAsync(context);

        LessonResource invalid = TestEntities.LessonResource(graph.LessonId, blobObjectName: null);
        invalid.Status = PublicationStatus.Published;
        context.LessonResources.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_LessonResources_PublishedRequiresBlob");
    }

    [Fact]
    public async Task SubscriptionPeriodEndingBeforeItStarts_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Subscription invalid =
            TestEntities.Subscription(graph.UserId, graph.MembershipOfferId, graph.PriceId);
        invalid.CurrentPeriodStartUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        invalid.CurrentPeriodEndUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        context.Subscriptions.Add(invalid);

        await AssertCheckViolationAsync(context, "CK_Subscriptions_PeriodOrdered");
    }

    /// <summary>
    /// Enum columns are constrained strings, so a value outside the enum must be rejected.
    /// EF cannot produce one, so this writes parameterised SQL — the value is always bound,
    /// never concatenated into the statement text.
    /// </summary>
    [Fact]
    public async Task EnumColumnRejectsValueOutsideTheEnum()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Course course = TestEntities.Course();
        context.Courses.Add(course);
        await context.SaveChangesAsync();

        SqlException exception = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(
            () => context.Database.ExecuteSqlRawAsync(
                "UPDATE [catalog].[Courses] SET [Status] = {0} WHERE [Id] = {1}",
                "NotARealStatus",
                course.Id));

        Assert.Contains("CK_Courses_Status", exception.Message, StringComparison.Ordinal);
    }

    private static async Task AssertCheckViolationAsync(
        DanielsDojoDbContext context,
        string expectedConstraintName)
    {
        DbUpdateException exception =
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains(
            expectedConstraintName,
            exception.InnerException?.Message ?? string.Empty,
            StringComparison.Ordinal);
    }
}
