using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Community;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Community;

/// <summary>
/// Exercises the member's own screens: the dashboard, the learning list, community setup, and
/// the privacy defaults that decide what other members can do.
/// </summary>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class MemberTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private ApiHarness _harness = null!;
    private TestActor _member = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _harness = ApiHarness.Create(fixture);
        _member = await _harness.SignInAsync();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ---------------------------------------------------------------- authorization

    [Theory]
    [InlineData("/api/v1/me/dashboard")]
    [InlineData("/api/v1/me/courses")]
    [InlineData("/api/v1/me/community/status")]
    [InlineData("/api/v1/me/community/profile")]
    public async Task AnonymousRequests_Are401(string path)
    {
        using HttpClient client = _harness.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------------------------------------------------------- dashboard

    [Fact]
    public async Task Dashboard_ReportsRealCountsAndIsHonestAboutPurchasing()
    {
        using HttpClient client = MemberClient();

        using JsonDocument dashboard = await client.GetJsonAsync("/api/v1/me/dashboard");
        JsonElement root = dashboard.RootElement;

        Assert.Equal(0, root.GetProperty("enrolledCourseCount").GetInt32());
        Assert.Equal(0, root.GetProperty("unreadNotificationCount").GetInt32());
        Assert.Equal(0, root.GetProperty("pendingFriendRequestCount").GetInt32());

        // Checkout is a later phase, and the dashboard says so rather than offering a button.
        Assert.False(root.GetProperty("purchasingAvailable").GetBoolean());

        // Community is closed until setup is complete.
        JsonElement community = root.GetProperty("community");
        Assert.False(community.GetProperty("granted").GetBoolean());
        Assert.Equal("SetupRequired", community.GetProperty("denial").GetString());
        Assert.False(community.GetProperty("profileExists").GetBoolean());
    }

    [Fact]
    public async Task Dashboard_NeverLeaksTheExternalIdentity()
    {
        using HttpClient client = MemberClient();

        using HttpResponseMessage response =
            await client.GetAsync(new Uri("/api/v1/me/dashboard", UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(Authentication.TestTokenIssuer.TenantId, body, StringComparison.Ordinal);
        Assert.DoesNotContain("oid", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MyCourses_IsEmptyUntilEnrollmentExists()
    {
        using HttpClient client = MemberClient();

        using JsonDocument courses = await client.GetJsonAsync("/api/v1/me/courses");

        Assert.Empty(courses.RootElement.EnumerateArray());
    }

    // ---------------------------------------------------------------- setup

    [Fact]
    public async Task CommunityProfile_Is404BeforeSetup()
    {
        using HttpClient client = MemberClient();

        using HttpResponseMessage response =
            await client.GetAsync(new Uri("/api/v1/me/community/profile", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Setup_RequiresBothGuidelinesAndEligibility()
    {
        using HttpClient client = MemberClient();

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/me/community/profile",
            new
            {
                Handle = "valid-handle",
                Bio = (string?)null,
                AcceptGuidelines = false,
                AttestEligibility = false,
            },
            HttpStatusCode.BadRequest);

        JsonElement errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("acceptGuidelines", out _));
        Assert.True(errors.TryGetProperty("attestEligibility", out _));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("-leading")]
    [InlineData("double__underscore")]
    [InlineData("emoji😀handle")]
    public async Task Setup_RefusesAnUnsafeHandle(string handle)
    {
        using HttpClient client = MemberClient();

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/me/community/profile",
            Setup(handle),
            HttpStatusCode.BadRequest);

        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("handle", out _));
    }

    [Fact]
    public async Task Setup_StartsFullyPrivateAndRecordsNoBirthDate()
    {
        using HttpClient client = MemberClient();

        using JsonDocument profile = await client.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/me/community/profile",
            Setup("first-member"),
            HttpStatusCode.OK);

        JsonElement root = profile.RootElement;
        Assert.False(root.GetProperty("isDiscoverable").GetBoolean());
        Assert.Equal("NoOne", root.GetProperty("friendRequestPolicy").GetString());
        Assert.Equal("NoOne", root.GetProperty("messagePolicy").GetString());
        Assert.True(root.GetProperty("participationReady").GetBoolean());
        Assert.True(root.GetProperty("eligibilityAttested").GetBoolean());

        // Eligibility is an attestation timestamp. No date of birth exists to leak.
        string body = root.GetRawText();
        Assert.DoesNotContain("birth", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dateOfBirth", body, StringComparison.OrdinalIgnoreCase);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommunityProfile stored =
            await context.CommunityProfiles.SingleAsync(entry => entry.UserId == _member.UserId);

        Assert.Equal(CommunityGuidelines.CurrentVersion, stored.GuidelinesVersion);
        Assert.NotNull(stored.GuidelinesAcceptedAtUtc);
        Assert.NotNull(stored.EligibilityAttestedAtUtc);
    }

    [Fact]
    public async Task Setup_RefusesADuplicateHandleRegardlessOfCase()
    {
        using HttpClient first = MemberClient();
        await first.SendJsonAsync(
            HttpMethod.Post, "/api/v1/me/community/profile", Setup("Taken-Handle"), HttpStatusCode.OK);

        TestActor other = await _harness.SignInAsync();
        using HttpClient second = _harness.CreateClient(other);

        using JsonDocument problem = await second.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/me/community/profile",
            Setup("taken-handle"),
            HttpStatusCode.BadRequest);

        Assert.Equal("platform.duplicate_value", problem.ProblemCode());
    }

    [Fact]
    public async Task Setup_CannotRunTwice()
    {
        using HttpClient client = MemberClient();
        await client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/me/community/profile", Setup("only-once"), HttpStatusCode.OK);

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/me/community/profile",
            Setup("second-attempt"),
            HttpStatusCode.Conflict);

        Assert.Equal("platform.duplicate_value", problem.ProblemCode());
    }

    // ---------------------------------------------------------------- privacy

    [Fact]
    public async Task PrivacySettings_AreOpenedOnlyByAnExplicitChange()
    {
        using HttpClient client = MemberClient();

        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/me/community/profile", Setup("opt-in"), HttpStatusCode.OK);

        string rowVersion = created.RootElement.GetProperty("rowVersion").GetString()!;

        using JsonDocument updated = await client.SendJsonAsync(
            HttpMethod.Put,
            "/api/v1/me/community/profile",
            new
            {
                Bio = "Learning in public.",
                IsDiscoverable = true,
                FriendRequestPolicy = "Everyone",
                MessagePolicy = "FriendsOnly",
                RowVersion = rowVersion,
            },
            HttpStatusCode.OK);

        Assert.True(updated.RootElement.GetProperty("isDiscoverable").GetBoolean());
        Assert.Equal("Everyone", updated.RootElement.GetProperty("friendRequestPolicy").GetString());
        Assert.Equal("FriendsOnly", updated.RootElement.GetProperty("messagePolicy").GetString());
    }

    [Fact]
    public async Task MessagePolicy_HasNoEveryoneOption()
    {
        using HttpClient client = MemberClient();

        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/me/community/profile", Setup("closed-dms"), HttpStatusCode.OK);

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Put,
            "/api/v1/me/community/profile",
            new
            {
                Bio = (string?)null,
                IsDiscoverable = false,
                FriendRequestPolicy = "NoOne",
                MessagePolicy = "Everyone",
                RowVersion = created.RootElement.GetProperty("rowVersion").GetString(),
            },
            HttpStatusCode.BadRequest);

        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("messagePolicy", out _));
    }

    [Fact]
    public async Task StalePrivacyWrite_Is409()
    {
        using HttpClient client = MemberClient();

        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/me/community/profile", Setup("stale-privacy"), HttpStatusCode.OK);

        string rowVersion = created.RootElement.GetProperty("rowVersion").GetString()!;

        await client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/me/community/profile", Privacy(rowVersion, true), HttpStatusCode.OK);

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Put,
            "/api/v1/me/community/profile",
            Privacy(rowVersion, false),
            HttpStatusCode.Conflict);

        Assert.Equal("platform.concurrency_conflict", problem.ProblemCode());
    }

    // ---------------------------------------------------------------- access evaluator

    [Fact]
    public async Task SuspendingAProfile_ClosesTheCommunityWithoutDeletingAnything()
    {
        using HttpClient client = MemberClient();
        await client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/me/community/profile", Setup("suspendable"), HttpStatusCode.OK);

        using JsonDocument granted = await client.GetJsonAsync("/api/v1/me/community/status");
        Assert.True(granted.RootElement.GetProperty("granted").GetBoolean());

        await using (DanielsDojoDbContext context = fixture.CreateContext())
        {
            CommunityProfile profile =
                await context.CommunityProfiles.SingleAsync(entry => entry.UserId == _member.UserId);
            profile.Status = CommunityProfileStatus.Suspended;
            await context.SaveChangesAsync();
        }

        using JsonDocument suspended = await client.GetJsonAsync("/api/v1/me/community/status");
        Assert.False(suspended.RootElement.GetProperty("granted").GetBoolean());
        Assert.Equal("Suspended", suspended.RootElement.GetProperty("denial").GetString());

        // The profile row itself survives, so nothing is lost by a moderation decision.
        await using DanielsDojoDbContext verify = fixture.CreateContext();
        Assert.True(await verify.CommunityProfiles.AnyAsync(entry => entry.UserId == _member.UserId));
    }

    // ---------------------------------------------------------------- helpers

    private HttpClient MemberClient() => _harness.CreateClient(_member);

    private static object Setup(string handle) => new
    {
        Handle = handle,
        Bio = (string?)null,
        AcceptGuidelines = true,
        AttestEligibility = true,
    };

    private static object Privacy(string rowVersion, bool discoverable) => new
    {
        Bio = (string?)null,
        IsDiscoverable = discoverable,
        FriendRequestPolicy = "NoOne",
        MessagePolicy = "NoOne",
        RowVersion = rowVersion,
    };
}
