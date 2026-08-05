using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Infrastructure.Commerce;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Catalog;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DanielsDojo.IntegrationTests.Commerce;

/// <summary>
/// The purchase path end to end with the deterministic payment provider.
/// </summary>
/// <remarks>
/// The cases that matter are the ones where money and access could disagree: a browser
/// claiming success for an unpaid session, a webhook and a redirect racing to settle the same
/// order, and a membership purchase whose entitlement must trace to a real subscription row.
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class CheckoutTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private ApiHarness _harness = null!;
    private TestActor _customer = null!;
    private Guid _membershipOfferId;
    private Guid _lifetimeOfferId;
    private Guid _courseId;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        _harness = ApiHarness.Create(fixture);
        _customer = await _harness.SignInAsync();

        await SeedAsync();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ---------------------------------------------------------------- checkout

    [Fact]
    public async Task BuyingAMembershipGrantsAccessOnlyAfterTheProviderConfirmsPayment()
    {
        using HttpClient customer = _harness.CreateClient(_customer);

        using JsonDocument started = await customer.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/billing/checkout",
            new { offerId = _membershipOfferId },
            HttpStatusCode.OK);

        Assert.False(string.IsNullOrWhiteSpace(started.RootElement.GetProperty("checkoutUrl").GetString()));

        string sessionId = await SessionIdAsync();

        // The browser comes back claiming success before anything was paid. The provider is
        // asked, disagrees, and nothing is granted.
        using JsonDocument unpaid = await customer.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/billing/checkout/{sessionId}/confirm",
            null,
            HttpStatusCode.OK);

        Assert.False(unpaid.RootElement.GetProperty("confirmed").GetBoolean());
        Assert.False(unpaid.RootElement.GetProperty("entitlementGranted").GetBoolean());

        await using (DanielsDojoDbContext context = fixture.CreateContext())
        {
            Assert.Empty(await context.Entitlements
                .Where(grant => grant.UserId == _customer.UserId)
                .ToListAsync());
        }

        // The customer actually pays.
        Provider().CompleteCheckout(sessionId);

        using JsonDocument paid = await customer.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/billing/checkout/{sessionId}/confirm",
            null,
            HttpStatusCode.OK);

        Assert.True(paid.RootElement.GetProperty("confirmed").GetBoolean());
        Assert.True(paid.RootElement.GetProperty("entitlementGranted").GetBoolean());

        await using (DanielsDojoDbContext context = fixture.CreateContext())
        {
            Entitlement grant = await context.Entitlements.SingleAsync(
                candidate => candidate.UserId == _customer.UserId);

            // The grant traces to a real subscription row, never floats free.
            Assert.Equal(EntitlementScope.AllMembershipCourses, grant.Scope);
            Assert.Equal(EntitlementSource.Subscription, grant.Source);
            Assert.NotNull(grant.SubscriptionId);
            Assert.NotNull(grant.EndsAtUtc);

            Order order = await context.Orders.SingleAsync(
                candidate => candidate.UserId == _customer.UserId);
            Assert.Equal(OrderStatus.Paid, order.Status);
        }

        // And the access evaluator now grants the membership course.
        using JsonDocument curriculum = await customer.GetJsonAsync(
            "/api/v1/learning/courses/commerce-course");
        Assert.True(curriculum.RootElement.GetProperty("accessGranted").GetBoolean());
        Assert.Equal("Membership", curriculum.RootElement.GetProperty("accessReason").GetString());
    }

    [Fact]
    public async Task BuyingACourseOutrightGrantsALifetimeEntitlementTracedToTheOrderLine()
    {
        using HttpClient customer = _harness.CreateClient(_customer);

        await customer.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/billing/checkout",
            new { offerId = _lifetimeOfferId },
            HttpStatusCode.OK);

        string sessionId = await SessionIdAsync();
        Provider().CompleteCheckout(sessionId);

        await customer.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/billing/checkout/{sessionId}/confirm",
            null,
            HttpStatusCode.OK);

        await using DanielsDojoDbContext context = fixture.CreateContext();

        Entitlement grant = await context.Entitlements.SingleAsync(
            candidate => candidate.UserId == _customer.UserId);

        Assert.Equal(EntitlementScope.Course, grant.Scope);
        Assert.Equal(EntitlementSource.Purchase, grant.Source);
        Assert.Equal(_courseId, grant.CourseId);
        Assert.NotNull(grant.OrderItemId);
        Assert.Null(grant.EndsAtUtc);
    }

    [Fact]
    public async Task TheWebhookAndTheRedirectSettleTheSameOrderExactlyOnce()
    {
        using HttpClient customer = _harness.CreateClient(_customer);

        await customer.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/billing/checkout",
            new { offerId = _membershipOfferId },
            HttpStatusCode.OK);

        string sessionId = await SessionIdAsync();
        Provider().CompleteCheckout(sessionId);

        // The webhook arrives first...
        (string payload, string signature) = Provider().CreateCheckoutCompletedNotification(sessionId);
        await PostWebhookAsync(payload, signature, HttpStatusCode.Accepted);

        // ...then the browser confirms, then the webhook is redelivered.
        await customer.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/billing/checkout/{sessionId}/confirm",
            null,
            HttpStatusCode.OK);
        await PostWebhookAsync(payload, signature, HttpStatusCode.Accepted);

        await using DanielsDojoDbContext context = fixture.CreateContext();

        // One entitlement, one subscription, one paid order. Not three of anything.
        Assert.Single(await context.Entitlements
            .Where(grant => grant.UserId == _customer.UserId).ToListAsync());
        Assert.Single(await context.Subscriptions
            .Where(subscription => subscription.UserId == _customer.UserId).ToListAsync());
        Assert.Single(await context.WebhookEvents
            .Where(seen => seen.Provider == "Stripe").ToListAsync());
    }

    [Fact]
    public async Task AnUnsignedPaymentNotificationChangesNothing()
    {
        using HttpClient customer = _harness.CreateClient(_customer);

        await customer.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/billing/checkout",
            new { offerId = _membershipOfferId },
            HttpStatusCode.OK);

        string sessionId = await SessionIdAsync();
        Provider().CompleteCheckout(sessionId);

        (string payload, _) = Provider().CreateCheckoutCompletedNotification(sessionId);
        await PostWebhookAsync(payload, signature: null, HttpStatusCode.Unauthorized);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Empty(await context.Entitlements
            .Where(grant => grant.UserId == _customer.UserId).ToListAsync());
    }

    [Fact]
    public async Task ConfirmingSomebodyElsesSessionIsNotFound()
    {
        using HttpClient customer = _harness.CreateClient(_customer);

        await customer.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/billing/checkout",
            new { offerId = _membershipOfferId },
            HttpStatusCode.OK);

        string sessionId = await SessionIdAsync();
        Provider().CompleteCheckout(sessionId);

        TestActor stranger = await _harness.SignInAsync();
        using HttpClient strangerClient = _harness.CreateClient(stranger);

        using HttpResponseMessage response = await strangerClient.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/billing/checkout/{sessionId}/confirm",
            null);

        // Not found rather than forbidden: the session's existence is nobody else's business,
        // and no entitlement lands with the stranger either.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Empty(await context.Entitlements
            .Where(grant => grant.UserId == stranger.UserId).ToListAsync());
    }

    [Fact]
    public async Task BuyingSomethingAlreadyOwnedIsRefusedBeforeAnyCheckoutIsCreated()
    {
        using HttpClient customer = _harness.CreateClient(_customer);

        await customer.SendJsonAsync(
            HttpMethod.Post, "/api/v1/billing/checkout",
            new { offerId = _membershipOfferId }, HttpStatusCode.OK);

        string sessionId = await SessionIdAsync();
        Provider().CompleteCheckout(sessionId);
        await customer.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/billing/checkout/{sessionId}/confirm", null, HttpStatusCode.OK);

        using JsonDocument problem = await customer.SendJsonAsync(
            HttpMethod.Post, "/api/v1/billing/checkout",
            new { offerId = _membershipOfferId }, HttpStatusCode.Conflict);

        Assert.Equal("commerce.already_owned", problem.ProblemCode());
    }

    [Fact]
    public async Task BillingShowsTheMembershipAndTheOrderHistory()
    {
        using HttpClient customer = _harness.CreateClient(_customer);

        await customer.SendJsonAsync(
            HttpMethod.Post, "/api/v1/billing/checkout",
            new { offerId = _membershipOfferId }, HttpStatusCode.OK);

        string sessionId = await SessionIdAsync();
        Provider().CompleteCheckout(sessionId);
        await customer.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/billing/checkout/{sessionId}/confirm", null, HttpStatusCode.OK);

        using JsonDocument billing = await customer.GetJsonAsync("/api/v1/billing/");

        Assert.Equal(
            "Active",
            billing.RootElement.GetProperty("membership").GetProperty("status").GetString());

        JsonElement order = billing.RootElement.GetProperty("orders").EnumerateArray().Single();
        Assert.Equal("Paid", order.GetProperty("status").GetString());
        Assert.Equal(999, order.GetProperty("totalMinor").GetInt64());
    }

    [Fact]
    public async Task ASubscriptionLapseReportedByTheProviderExpiresTheEntitlement()
    {
        using HttpClient customer = _harness.CreateClient(_customer);

        await customer.SendJsonAsync(
            HttpMethod.Post, "/api/v1/billing/checkout",
            new { offerId = _membershipOfferId }, HttpStatusCode.OK);

        string sessionId = await SessionIdAsync();
        Provider().CompleteCheckout(sessionId);
        await customer.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/billing/checkout/{sessionId}/confirm", null, HttpStatusCode.OK);

        string subscriptionId;

        await using (DanielsDojoDbContext context = fixture.CreateContext())
        {
            subscriptionId = (await context.Subscriptions.SingleAsync(
                subscription => subscription.UserId == _customer.UserId)).StripeSubscriptionId;
        }

        // The provider now reports the subscription canceled with a boundary in the past.
        DateTimeOffset past = DateTimeOffset.UtcNow.AddDays(-1);

        Provider().SetSubscription(new Application.Commerce.SubscriptionState(
            subscriptionId, "canceled", past.AddMonths(-1), past,
            CancelAtPeriodEnd: true, CanceledAt: past.AddDays(-5), EndedAt: past));

        string payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            id = $"evt_sub_lapsed_{Guid.NewGuid():N}",
            type = "customer.subscription.deleted",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            data = new { @object = new { id = subscriptionId, @object = "subscription" } },
        });

        await PostWebhookAsync(payload, Sign(payload), HttpStatusCode.Accepted);

        await using (DanielsDojoDbContext context = fixture.CreateContext())
        {
            Entitlement grant = await context.Entitlements.SingleAsync(
                candidate => candidate.UserId == _customer.UserId);

            Assert.Equal(EntitlementStatus.Expired, grant.Status);
        }

        // Provider-verified state has flowed all the way to the door: access is now refused.
        using JsonDocument curriculum = await customer.GetJsonAsync(
            "/api/v1/learning/courses/commerce-course");
        Assert.False(curriculum.RootElement.GetProperty("accessGranted").GetBoolean());
    }

    // ------------------------------------------------------- public storefront path

    [Fact]
    public async Task TheOfferIdOnThePublicPageLeadsAllTheWayToAccess()
    {
        // The public course page carries the offer identifier a buy button needs…
        using HttpClient anonymous = _harness.Factory.CreateClient();
        Guid publicOfferId;

        using (JsonDocument page = await anonymous.GetJsonAsync(
            "/api/v1/catalog/courses/commerce-course"))
        {
            publicOfferId = page.RootElement
                .GetProperty("lifetimePrice").GetProperty("offerId").GetGuid();
        }

        Assert.Equal(_lifetimeOfferId, publicOfferId);

        // …and the membership price is published on its own endpoint for the pricing page.
        // (Which membership offer is "current" when several exist is the resolver's own
        // concern; the pricing page only needs a purchasable identifier and real amount.)
        using (JsonDocument membership = await anonymous.GetJsonAsync("/api/v1/catalog/membership"))
        {
            Assert.NotEqual(Guid.Empty, membership.RootElement.GetProperty("offerId").GetGuid());
            Assert.True(membership.RootElement.GetProperty("amountMinor").GetInt64() > 0);
        }

        // Checkout starts from exactly that public identifier.
        using HttpClient customer = _harness.CreateClient(_customer);
        using JsonDocument started = await customer.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/billing/checkout",
            new { offerId = publicOfferId },
            HttpStatusCode.OK);

        string sessionId = await SessionIdAsync();

        // The deterministic "pay" happens over HTTP, exactly as the stand-in page does it.
        using (JsonDocument _ = await customer.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/billing/deterministic/{sessionId}/pay",
            null,
            HttpStatusCode.NoContent))
        {
        }

        using JsonDocument confirmed = await customer.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/billing/checkout/{sessionId}/confirm",
            null,
            HttpStatusCode.OK);

        Assert.True(confirmed.RootElement.GetProperty("entitlementGranted").GetBoolean());

        using JsonDocument curriculum = await customer.GetJsonAsync(
            "/api/v1/learning/courses/commerce-course");
        Assert.True(curriculum.RootElement.GetProperty("accessGranted").GetBoolean());

        // The settle wrote exactly one purchase notification alongside the entitlement.
        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Equal(1, await context.Notifications.CountAsync(
            notification => notification.RecipientUserId == _customer.UserId
                && notification.Kind == Domain.Community.NotificationKind.PurchaseCompleted));
    }

    // ---------------------------------------------------------------- helpers

    private DeterministicPaymentProvider Provider() =>
        _harness.Factory.Services.GetRequiredService<DeterministicPaymentProvider>();

    private async Task<string> SessionIdAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        return (await context.Orders
            .Where(order => order.UserId == _customer.UserId)
            .OrderByDescending(order => order.CreatedAtUtc)
            .FirstAsync()).StripeCheckoutSessionId!;
    }

    private async Task PostWebhookAsync(string payload, string? signature, HttpStatusCode expected)
    {
        using HttpClient anonymous = _harness.Factory.CreateClient();

        using HttpRequestMessage request = new(
            HttpMethod.Post,
            new Uri("/api/v1/billing/webhooks/stripe", UriKind.Relative))
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
        };

        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation("Stripe-Signature", signature);
        }

        using HttpResponseMessage response = await anonymous.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
    }

    private static string Sign(string payload)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        byte[] signature = System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                DeterministicPaymentProvider.DeterministicWebhookSecret),
            System.Text.Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));

        return $"t={timestamp},v1={Convert.ToHexString(signature).ToLowerInvariant()}";
    }

    private async Task SeedAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        Course course = CatalogFactory.Course(
            "commerce-course", "Commerce course", PublicationStatus.Published, true, now);
        CourseSection section = CatalogFactory.Section(
            course.Id, "Section", 0, PublicationStatus.Published, now);
        Lesson lesson = CatalogFactory.Lesson(
            course.Id, section.Id, "lesson-one", 0,
            PublicationStatus.Published, LessonType.Article, false, "Body.", now);

        context.Courses.Add(course);
        context.CourseSections.Add(section);
        context.Lessons.Add(lesson);

        OfferPrice membership = CommerceFactory.MembershipOffer(
            context, $"membership-{Guid.NewGuid():N}", now);
        OfferPrice lifetime = CommerceFactory.LifetimeOffer(context, course.Id, now);

        await context.SaveChangesAsync();

        _membershipOfferId = membership.OfferId;
        _lifetimeOfferId = lifetime.OfferId;
        _courseId = course.Id;
    }
}
