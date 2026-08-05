using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Catalog;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Admin;

/// <summary>
/// The operator's back office.
/// </summary>
/// <remarks>
/// The rules that matter: every mutation needs a reason and lands in the audit trail, an
/// operator can never remove their own key, a manual grant produces real access, and the
/// kill switches actually kill what they claim to — while a missing flag row means "on".
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class AdminOperationsTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private ApiHarness _harness = null!;
    private TestActor _admin = null!;
    private TestActor _member = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        _harness = ApiHarness.Create(fixture);
        _admin = await _harness.SignInAsync(admin: true);
        _member = await _harness.SignInAsync();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task RolesCanBeGrantedButNeverRemovedFromYourself()
    {
        using HttpClient admin = _harness.CreateClient(_admin);

        // Grant Admin to the member, with the recorded reason.
        using (JsonDocument granted = await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/users/{_member.UserId}/admin-role",
            new { IsAdmin = true, Reason = "Second operator for launch." },
            HttpStatusCode.OK))
        {
            List<string?> roles = [.. granted.RootElement.GetProperty("roles").EnumerateArray()
                .Select(role => role.GetString())];
            Assert.Contains("Admin", roles);
        }

        // Nobody can touch their own Admin role, so the last key cannot be locked inside.
        using HttpResponseMessage refused = await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/users/{_admin.UserId}/admin-role",
            new { IsAdmin = false, Reason = "Trying to demote myself." });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        // A missing reason is refused, because the audit row would say nothing.
        using HttpResponseMessage noReason = await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/users/{_member.UserId}/admin-role",
            new { IsAdmin = false, Reason = " " });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.True(await context.AuditLogs.AnyAsync(
            entry => entry.Action == "Identity.Role.AdminGranted"));
    }

    [Fact]
    public async Task DisablingAnAccountLocksItOutButNeverYourOwn()
    {
        using HttpClient admin = _harness.CreateClient(_admin);

        using (JsonDocument disabled = await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/users/{_member.UserId}/status",
            new { TargetStatus = "Disabled", Reason = "Chargeback abuse." },
            HttpStatusCode.OK))
        {
            Assert.Equal("Disabled", disabled.RootElement.GetProperty("status").GetString());
        }

        // The disabled member's requests are refused by provisioning.
        using (HttpClient member = _harness.CreateClient(_member))
        {
            using HttpResponseMessage refused = await member.GetAsync(
                new Uri("/api/v1/me/dashboard", UriKind.Relative));
            Assert.NotEqual(HttpStatusCode.OK, refused.StatusCode);
        }

        using HttpResponseMessage self = await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/users/{_admin.UserId}/status",
            new { TargetStatus = "Disabled", Reason = "Oops." });
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
    }

    [Fact]
    public async Task AManualGrantProducesRealAccess()
    {
        Guid courseId = await SeedCourseAsync("granted-course");

        using HttpClient admin = _harness.CreateClient(_admin);
        using (JsonDocument _ = await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/users/{_member.UserId}/grants",
            new { CourseId = courseId, Reason = "Scholarship." },
            HttpStatusCode.OK))
        {
        }

        // Granting twice is refused rather than stacking rows.
        using HttpResponseMessage duplicate = await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/users/{_member.UserId}/grants",
            new { CourseId = courseId, Reason = "Twice." });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using HttpClient member = _harness.CreateClient(_member);
        using JsonDocument curriculum = await member.GetJsonAsync(
            "/api/v1/learning/courses/granted-course");
        Assert.True(curriculum.RootElement.GetProperty("accessGranted").GetBoolean());
    }

    [Fact]
    public async Task TheCheckoutKillSwitchRefusesNewCheckoutsUntilReEnabled()
    {
        using HttpClient admin = _harness.CreateClient(_admin);

        using (JsonDocument off = await admin.SendJsonAsync(
            HttpMethod.Put,
            "/api/v1/admin/flags/checkout",
            new { Enabled = false, Reason = "Payment provider incident." },
            HttpStatusCode.OK))
        {
            Assert.False(off.RootElement.GetProperty("enabled").GetBoolean());
        }

        using HttpClient member = _harness.CreateClient(_member);
        using (JsonDocument refused = await member.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/billing/checkout",
            new { OfferId = Guid.NewGuid() },
            HttpStatusCode.Conflict))
        {
            Assert.Equal("commerce.provider_disabled", refused.ProblemCode());
        }

        // Unknown switches cannot be created.
        using HttpResponseMessage unknown = await admin.SendJsonAsync(
            HttpMethod.Put,
            "/api/v1/admin/flags/does-not-exist",
            new { Enabled = false, Reason = "Testing." });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // Back on: the refusal is gone (this offer id is bogus, so a 400/404-style refusal
        // for the offer itself is the expected next failure, not the kill switch).
        using (JsonDocument _ = await admin.SendJsonAsync(
            HttpMethod.Put,
            "/api/v1/admin/flags/checkout",
            new { Enabled = true, Reason = "Incident resolved." },
            HttpStatusCode.OK))
        {
        }

        using HttpResponseMessage after = await member.SendJsonAsync(
            HttpMethod.Post, "/api/v1/billing/checkout", new { OfferId = Guid.NewGuid() });
        string body = await after.Content.ReadAsStringAsync();
        Assert.DoesNotContain("provider_disabled", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOpsSnapshotAndListingsAreAdminOnly()
    {
        using HttpClient admin = _harness.CreateClient(_admin);

        using (JsonDocument ops = await admin.GetJsonAsync("/api/v1/admin/ops"))
        {
            Assert.True(ops.RootElement.GetProperty("databaseReachable").GetBoolean());
            Assert.Equal(0, ops.RootElement.GetProperty("pendingMigrationCount").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(
                ops.RootElement.GetProperty("lastAppliedMigration").GetString()));
            Assert.Equal(
                "Deterministic",
                ops.RootElement.GetProperty("paymentProviderMode").GetString());
        }

        using (JsonDocument flags = await admin.GetJsonAsync("/api/v1/admin/flags"))
        {
            // Defaults: both known switches exist and are on before any row is stored.
            List<JsonElement> items = [.. flags.RootElement.EnumerateArray()];
            Assert.Equal(2, items.Count);
            Assert.All(items, flag => Assert.True(flag.GetProperty("enabled").GetBoolean()));
        }

        using (JsonDocument users = await admin.GetJsonAsync("/api/v1/admin/users?search="))
        {
            Assert.True(users.RootElement.GetProperty("totalCount").GetInt32() >= 2);
        }

        using HttpClient member = _harness.CreateClient(_member);

        foreach (string route in (string[])
            ["/api/v1/admin/ops", "/api/v1/admin/users", "/api/v1/admin/flags",
             "/api/v1/admin/orders", "/api/v1/admin/audit", "/api/v1/admin/certificates",
             "/api/v1/admin/webhook-events"])
        {
            using HttpResponseMessage refused = await member.GetAsync(
                new Uri(route, UriKind.Relative));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }
    }

    [Fact]
    public async Task TheCommunityWritesSwitchMakesTheCommunityReadOnly()
    {
        // A fully set-up member…
        using (HttpClient member = _harness.CreateClient(_member))
        {
            using JsonDocument _ = await member.SendJsonAsync(
                HttpMethod.Post,
                "/api/v1/me/community/profile",
                new
                {
                    Handle = "paused-member",
                    Bio = (string?)null,
                    AcceptGuidelines = true,
                    AttestEligibility = true,
                },
                HttpStatusCode.OK);
        }

        using HttpClient admin = _harness.CreateClient(_admin);
        using (JsonDocument _ = await admin.SendJsonAsync(
            HttpMethod.Put,
            "/api/v1/admin/flags/community-writes",
            new { Enabled = false, Reason = "Spam wave." },
            HttpStatusCode.OK))
        {
        }

        // …can still read, but not write, and the message says why.
        using HttpClient paused = _harness.CreateClient(_member);
        using JsonDocument categories = await paused.GetJsonAsync("/api/v1/community/categories");
        Assert.NotNull(categories);

        using JsonDocument refused = await paused.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/community/threads",
            new { CategorySlug = "general", Title = "Held", Body = "Held back." },
            HttpStatusCode.Forbidden);
        Assert.Equal("community.forbidden", refused.ProblemCode());
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Guid> SeedCourseAsync(string slug)
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Course course = CatalogFactory.Course(
            slug, "Granted course", PublicationStatus.Published, false, now);
        CourseSection section = CatalogFactory.Section(
            course.Id, "Section", 0, PublicationStatus.Published, now);
        Lesson lesson = CatalogFactory.Lesson(
            course.Id, section.Id, "one", 0,
            PublicationStatus.Published, LessonType.Article, false, "Body.", now);

        context.Courses.Add(course);
        context.CourseSections.Add(section);
        context.Lessons.Add(lesson);
        await context.SaveChangesAsync();

        return course.Id;
    }
}
