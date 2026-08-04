using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Catalog;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Commerce;

/// <summary>
/// Exercises offer and price management: the Admin gate, immutability after activation,
/// the commerce rules, and the guarantee that no provider identifier can be set over HTTP.
/// </summary>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class AdminPricingTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private const string Base = "/api/v1/admin/pricing";

    private ApiHarness _harness = null!;
    private TestActor _admin = null!;

    public async Task InitializeAsync()
    {
        // The reference seed installs the roles first sign-in assigns.
        await fixture.ResetAsync();

        _harness = ApiHarness.Create(fixture);
        _admin = await _harness.SignInAsync(admin: true);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ---------------------------------------------------------------- authorization

    [Fact]
    public async Task Student_CannotReachPricing()
    {
        TestActor student = await _harness.SignInAsync();
        using HttpClient client = _harness.CreateClient(student);

        using HttpResponseMessage response =
            await client.GetAsync(new Uri($"{Base}/offers", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------------------------------------------------------------- offer shape rules

    [Fact]
    public async Task MembershipOffer_CannotNameACourse()
    {
        using HttpClient client = AdminClient();
        Guid courseId = await SeedCourseAsync();

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/offers",
            new
            {
                Code = "membership-invalid",
                Name = "Membership",
                Description = "All access.",
                Kind = "Membership",
                CourseId = courseId,
            },
            HttpStatusCode.BadRequest);

        Assert.Equal("commerce.rule_violation", problem.ProblemCode());
    }

    [Fact]
    public async Task LifetimeOffer_MustNameAnExistingCourse()
    {
        using HttpClient client = AdminClient();

        using JsonDocument missing = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/offers",
            NewLifetimeOffer("lifetime-no-course", courseId: null),
            HttpStatusCode.BadRequest);

        Assert.Equal("commerce.rule_violation", missing.ProblemCode());

        using JsonDocument unknown = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/offers",
            NewLifetimeOffer("lifetime-unknown-course", Guid.NewGuid()),
            HttpStatusCode.BadRequest);

        Assert.Equal("platform.validation_failed", unknown.ProblemCode());
    }

    [Fact]
    public async Task MembershipPrice_MustBeMonthlyAndLifetimeMustBeOneTime()
    {
        using HttpClient client = AdminClient();
        OfferHandle membership = await CreateMembershipAsync(client, "membership-interval");

        using JsonDocument wrongInterval = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/offers/{membership.Id}/prices",
            NewPrice(999, "USD", "OneTime"),
            HttpStatusCode.BadRequest);

        Assert.Equal("commerce.rule_violation", wrongInterval.ProblemCode());

        Guid courseId = await SeedCourseAsync();
        OfferHandle lifetime = await CreateLifetimeAsync(client, "lifetime-interval", courseId);

        using JsonDocument recurring = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/offers/{lifetime.Id}/prices",
            NewPrice(4999, "USD", "Month"),
            HttpStatusCode.BadRequest);

        Assert.Equal("commerce.rule_violation", recurring.ProblemCode());
    }

    // ---------------------------------------------------------------- provider isolation

    [Fact]
    public async Task ProviderIdentifiers_CannotBeSetOverHttp()
    {
        using HttpClient client = AdminClient();

        // A body carrying provider keys is accepted as an ordinary create; the extra members
        // simply do not bind, because the contracts have no field for them.
        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/offers",
            new
            {
                Code = "provider-attempt",
                Name = "Provider attempt",
                Description = "Attempting to claim a provider product.",
                Kind = "Membership",
                CourseId = (Guid?)null,
                StripeProductId = "prod_attacker_supplied",
            },
            HttpStatusCode.Created);

        Guid offerId = created.RootElement.GetProperty("id").GetGuid();
        Assert.False(created.RootElement.GetProperty("providerLinked").GetBoolean());

        using JsonDocument withPrice = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/offers/{offerId}/prices",
            new
            {
                AmountMinor = 999,
                Currency = "USD",
                BillingInterval = "Month",
                EffectiveFromUtc = DateTimeOffset.UtcNow,
                StripePriceId = "price_attacker_supplied",
            },
            HttpStatusCode.OK);

        Assert.Single(withPrice.RootElement.GetProperty("prices").EnumerateArray());

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Offer stored = await context.Offers
            .Include(offer => offer.Prices)
            .SingleAsync(offer => offer.Id == offerId);

        Assert.Null(stored.StripeProductId);
        Assert.All(stored.Prices, price => Assert.Null(price.StripePriceId));

        // Nothing the caller supplied about the provider is echoed back either.
        string body = created.RootElement.GetRawText();
        Assert.DoesNotContain("prod_attacker_supplied", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stripeProductId", body, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- immutability

    [Fact]
    public async Task ActivePrice_CannotBeEdited()
    {
        using HttpClient client = AdminClient();
        OfferHandle offer = await CreateMembershipAsync(client, "immutable-price");
        PriceHandle price = await AddPriceAsync(client, offer.Id, 999, "USD", "Month");
        PriceHandle active = await ActivatePriceAsync(client, offer.Id, price);

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/offers/{offer.Id}/prices/{active.Id}",
            new
            {
                AmountMinor = 1,
                Currency = "USD",
                BillingInterval = "Month",
                EffectiveFromUtc = DateTimeOffset.UtcNow,
                active.RowVersion,
            },
            HttpStatusCode.BadRequest);

        Assert.Equal("commerce.price_immutable", problem.ProblemCode());

        // The stored amount is untouched.
        await using DanielsDojoDbContext context = fixture.CreateContext();
        Price stored = await context.Prices.SingleAsync(entity => entity.Id == active.Id);
        Assert.Equal(999, stored.AmountMinor);
    }

    [Fact]
    public async Task OfferCode_IsFixedOnceTheOfferIsActive()
    {
        using HttpClient client = AdminClient();
        OfferHandle offer = await CreateMembershipAsync(client, "fixed-code");
        PriceHandle price = await AddPriceAsync(client, offer.Id, 999, "USD", "Month");
        await ActivatePriceAsync(client, offer.Id, price);
        OfferHandle live = await ActivateOfferAsync(client, offer.Id);

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/offers/{offer.Id}",
            new
            {
                Code = "renamed-code",
                Name = "Membership",
                Description = "All access.",
                CourseId = (Guid?)null,
                live.RowVersion,
            },
            HttpStatusCode.BadRequest);

        Assert.Equal("commerce.rule_violation", problem.ProblemCode());

        // The display name is still editable, which is the point of separating the two.
        using JsonDocument renamed = await client.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/offers/{offer.Id}",
            new
            {
                Code = "fixed-code",
                Name = "Membership (renamed)",
                Description = "All access.",
                CourseId = (Guid?)null,
                live.RowVersion,
            },
            HttpStatusCode.OK);

        Assert.Equal("Membership (renamed)", renamed.RootElement.GetProperty("name").GetString());
    }

    // ---------------------------------------------------------------- status rules

    [Fact]
    public async Task OfferCannotGoLiveWithoutAnActivePrice()
    {
        using HttpClient client = AdminClient();
        OfferHandle offer = await CreateMembershipAsync(client, "no-price-offer");

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/offers/{offer.Id}/status/Active",
            new { Reason = "Going live.", offer.RowVersion },
            HttpStatusCode.BadRequest);

        Assert.Equal("commerce.rule_violation", problem.ProblemCode());
    }

    [Fact]
    public async Task RetiredIsTerminal()
    {
        using HttpClient client = AdminClient();
        OfferHandle offer = await CreateMembershipAsync(client, "terminal-offer");

        using JsonDocument retired = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/offers/{offer.Id}/status/Retired",
            new { Reason = "Never launched.", offer.RowVersion },
            HttpStatusCode.OK);

        string version = retired.RootElement.GetProperty("rowVersion").GetString()!;

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/offers/{offer.Id}/status/Active",
            new { Reason = "Bringing it back.", RowVersion = version },
            HttpStatusCode.BadRequest);

        Assert.Equal("commerce.rule_violation", problem.ProblemCode());
    }

    [Fact]
    public async Task OnlyOnePriceCanBeActiveAtATime()
    {
        using HttpClient client = AdminClient();
        OfferHandle offer = await CreateMembershipAsync(client, "single-active-price");

        PriceHandle first = await AddPriceAsync(client, offer.Id, 999, "USD", "Month");
        await ActivatePriceAsync(client, offer.Id, first);

        PriceHandle second = await AddPriceAsync(client, offer.Id, 1299, "USD", "Month");

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/offers/{offer.Id}/prices/{second.Id}/status/Active",
            new { Reason = "Price rise.", second.RowVersion },
            HttpStatusCode.BadRequest);

        Assert.Equal("commerce.rule_violation", problem.ProblemCode());
    }

    [Fact]
    public async Task RetiringAPriceRecordsWhenItStopped()
    {
        using HttpClient client = AdminClient();
        OfferHandle offer = await CreateMembershipAsync(client, "retire-price");
        PriceHandle price = await AddPriceAsync(client, offer.Id, 999, "USD", "Month");
        PriceHandle active = await ActivatePriceAsync(client, offer.Id, price);

        using JsonDocument retired = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/offers/{offer.Id}/prices/{active.Id}/status/Retired",
            new { Reason = "Superseded.", active.RowVersion },
            HttpStatusCode.OK);

        JsonElement stored = retired.RootElement.GetProperty("prices")[0];
        Assert.Equal("Retired", stored.GetProperty("status").GetString());
        Assert.False(stored.GetProperty("editable").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, stored.GetProperty("retiredAtUtc").ValueKind);
    }

    // ---------------------------------------------------------------- concurrency and audit

    [Fact]
    public async Task StaleOfferWrite_Is409()
    {
        using HttpClient client = AdminClient();
        OfferHandle offer = await CreateMembershipAsync(client, "stale-offer");

        using JsonDocument first = await client.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/offers/{offer.Id}",
            UpdateOffer("stale-offer", "First name", offer.RowVersion),
            HttpStatusCode.OK);

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/offers/{offer.Id}",
            UpdateOffer("stale-offer", "Second name", offer.RowVersion),
            HttpStatusCode.Conflict);

        Assert.Equal("platform.concurrency_conflict", problem.ProblemCode());
    }

    [Fact]
    public async Task PriceActivationIsAudited()
    {
        using HttpClient client = AdminClient();
        OfferHandle offer = await CreateMembershipAsync(client, "audited-price");
        PriceHandle price = await AddPriceAsync(client, offer.Id, 999, "USD", "Month");
        await ActivatePriceAsync(client, offer.Id, price);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        var entry = await context.AuditLogs.SingleAsync(log =>
            log.TargetId == price.Id.ToString("D") && log.Action == "Commerce.Price.StatusChanged");

        Assert.Equal("Approved for launch.", entry.Reason);
        Assert.Equal(_admin.UserId, entry.ActorUserId);
        Assert.Contains("Active", entry.MetadataJson, StringComparison.Ordinal);

        // Metadata carries identifiers and statuses only — never a customer or a payload.
        Assert.DoesNotContain("@", entry.MetadataJson, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private HttpClient AdminClient() => _harness.CreateClient(_admin);

    private static object NewMembershipOffer(string code) => new
    {
        Code = code,
        Name = "Membership",
        Description = "All access.",
        Kind = "Membership",
        CourseId = (Guid?)null,
    };

    private static object NewLifetimeOffer(string code, Guid? courseId) => new
    {
        Code = code,
        Name = "Lifetime",
        Description = "Lifetime access.",
        Kind = "CourseLifetime",
        CourseId = courseId,
    };

    private static object UpdateOffer(string code, string name, string rowVersion) => new
    {
        Code = code,
        Name = name,
        Description = "All access.",
        CourseId = (Guid?)null,
        RowVersion = rowVersion,
    };

    private static object NewPrice(long amountMinor, string currency, string interval) => new
    {
        AmountMinor = amountMinor,
        Currency = currency,
        BillingInterval = interval,
        EffectiveFromUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
    };

    private static async Task<OfferHandle> CreateMembershipAsync(HttpClient client, string code)
    {
        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/offers", NewMembershipOffer(code), HttpStatusCode.Created);

        return Handle(created);
    }

    private static async Task<OfferHandle> CreateLifetimeAsync(
        HttpClient client,
        string code,
        Guid courseId)
    {
        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/offers",
            NewLifetimeOffer(code, courseId),
            HttpStatusCode.Created);

        return Handle(created);
    }

    private static async Task<PriceHandle> AddPriceAsync(
        HttpClient client,
        Guid offerId,
        long amountMinor,
        string currency,
        string interval)
    {
        using JsonDocument withPrice = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/offers/{offerId}/prices",
            NewPrice(amountMinor, currency, interval),
            HttpStatusCode.OK);

        JsonElement price = withPrice.RootElement.GetProperty("prices")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("amountMinor").GetInt64() == amountMinor);

        return new PriceHandle(
            price.GetProperty("id").GetGuid(),
            price.GetProperty("rowVersion").GetString()!);
    }

    private static async Task<PriceHandle> ActivatePriceAsync(
        HttpClient client,
        Guid offerId,
        PriceHandle price)
    {
        using JsonDocument activated = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/offers/{offerId}/prices/{price.Id}/status/Active",
            new { Reason = "Approved for launch.", price.RowVersion },
            HttpStatusCode.OK);

        JsonElement stored = activated.RootElement.GetProperty("prices")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("id").GetGuid() == price.Id);

        return new PriceHandle(price.Id, stored.GetProperty("rowVersion").GetString()!);
    }

    private static async Task<OfferHandle> ActivateOfferAsync(HttpClient client, Guid offerId)
    {
        using JsonDocument current = await client.GetJsonAsync($"{Base}/offers/{offerId}");

        using JsonDocument activated = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/offers/{offerId}/status/Active",
            new
            {
                Reason = "Launch.",
                RowVersion = current.RootElement.GetProperty("rowVersion").GetString(),
            },
            HttpStatusCode.OK);

        return Handle(activated);
    }

    private static OfferHandle Handle(JsonDocument offer) => new(
        offer.RootElement.GetProperty("id").GetGuid(),
        offer.RootElement.GetProperty("rowVersion").GetString()!);

    private async Task<Guid> SeedCourseAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Course course = CatalogFactory.Course(
            $"priced-course-{Guid.NewGuid():N}",
            "Priced course",
            PublicationStatus.Published,
            includedInMembership: false,
            DateTimeOffset.UtcNow);

        context.Courses.Add(course);
        await context.SaveChangesAsync();

        return course.Id;
    }

    private sealed record OfferHandle(Guid Id, string RowVersion);

    private sealed record PriceHandle(Guid Id, string RowVersion);
}
