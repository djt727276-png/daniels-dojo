using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Catalog;
using DanielsDojo.IntegrationTests.Commerce;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Learning;

/// <summary>
/// Course-completion certificates.
/// </summary>
/// <remarks>
/// The rule under test: a certificate exists only because every published lesson was
/// completed, verifies publicly by its code alone, discloses nothing beyond what the
/// certificate itself prints, and revocation is an audit-friendly mark rather than a
/// deletion.
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class CertificateTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private ApiHarness _harness = null!;
    private TestActor _member = null!;
    private TestActor _admin = null!;
    private Guid _firstLessonId;
    private Guid _secondLessonId;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        _harness = ApiHarness.Create(fixture);
        _member = await _harness.SignInAsync();
        _admin = await _harness.SignInAsync(admin: true);

        await SeedAsync();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task CompletingEveryLessonEarnsAVerifiableCertificateExactlyOnce()
    {
        using HttpClient member = _harness.CreateClient(_member);

        await Complete(member, _firstLessonId);

        // One lesson in: nothing has been earned.
        using (JsonDocument early = await member.GetJsonAsync("/api/v1/learning/certificates"))
        {
            Assert.Empty(early.RootElement.EnumerateArray());
        }

        using JsonDocument last = await Complete(member, _secondLessonId);
        Assert.True(last.RootElement.GetProperty("courseCompleted").GetBoolean());

        // Completing again must not mint a second certificate.
        await Complete(member, _secondLessonId);

        using JsonDocument mine = await member.GetJsonAsync("/api/v1/learning/certificates");
        JsonElement certificate = mine.RootElement.EnumerateArray().Single();

        Assert.True(certificate.GetProperty("isValid").GetBoolean());
        Assert.Equal("Certificate course", certificate.GetProperty("courseTitle").GetString());

        string code = certificate.GetProperty("verificationCode").GetString()!;

        // Anyone holding the code can verify it, anonymously.
        using HttpClient anonymous = _harness.Factory.CreateClient();
        using JsonDocument verified = await anonymous.GetJsonAsync(
            $"/api/v1/certificates/{code}/verify");

        Assert.True(verified.RootElement.GetProperty("isValid").GetBoolean());
        Assert.Equal(
            "Certificate course", verified.RootElement.GetProperty("courseTitle").GetString());

        // And nothing beyond the certificate face is disclosed.
        string payload = verified.RootElement.GetRawText();
        Assert.DoesNotContain("email", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userId", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUnknownCodeVerifiesAsNotFound()
    {
        using HttpClient anonymous = _harness.Factory.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync(
            new Uri("/api/v1/certificates/DOESNOTEXIST/verify", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RevocationRequiresAReasonAndFlipsPublicVerification()
    {
        using HttpClient member = _harness.CreateClient(_member);
        await Complete(member, _firstLessonId);
        await Complete(member, _secondLessonId);

        using JsonDocument mine = await member.GetJsonAsync("/api/v1/learning/certificates");
        JsonElement certificate = mine.RootElement.EnumerateArray().Single();
        Guid id = certificate.GetProperty("id").GetGuid();
        string code = certificate.GetProperty("verificationCode").GetString()!;

        using HttpClient admin = _harness.CreateClient(_admin);

        // No reason, no revocation.
        await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/certificates/{id}/revoke",
            new { reason = " " },
            HttpStatusCode.BadRequest);

        await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/certificates/{id}/revoke",
            new { reason = "Completion evidence was invalidated." },
            HttpStatusCode.OK);

        using HttpClient anonymous = _harness.Factory.CreateClient();
        using JsonDocument verified = await anonymous.GetJsonAsync(
            $"/api/v1/certificates/{code}/verify");

        // The code still answers — as revoked, not as vanished.
        Assert.False(verified.RootElement.GetProperty("isValid").GetBoolean());
        Assert.NotEqual(
            JsonValueKind.Null, verified.RootElement.GetProperty("revokedAtUtc").ValueKind);
    }

    [Fact]
    public async Task AStudentCannotRevokeAnything()
    {
        using HttpClient member = _harness.CreateClient(_member);

        using HttpResponseMessage response = await member.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/certificates/{Guid.NewGuid()}/revoke",
            new { reason = "Nice try." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Task<JsonDocument> Complete(HttpClient client, Guid lessonId) =>
        client.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/learning/lessons/{lessonId}/progress",
            new { positionSeconds = 0, completed = true },
            HttpStatusCode.OK);

    private async Task SeedAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        Course course = CatalogFactory.Course(
            "certificate-course", "Certificate course", PublicationStatus.Published, true, now);
        CourseSection section = CatalogFactory.Section(
            course.Id, "Section", 0, PublicationStatus.Published, now);
        Lesson first = CatalogFactory.Lesson(
            course.Id, section.Id, "one", 0,
            PublicationStatus.Published, LessonType.Article, false, "Body.", now);
        Lesson second = CatalogFactory.Lesson(
            course.Id, section.Id, "two", 1,
            PublicationStatus.Published, LessonType.Article, false, "Body.", now);

        context.Courses.Add(course);
        context.CourseSections.Add(section);
        context.Lessons.AddRange(first, second);

        OfferPrice membership = CommerceFactory.MembershipOffer(
            context, $"membership-{Guid.NewGuid():N}", now);
        Guid subscriptionId = CommerceFactory.Subscription(
            context, _member.UserId, membership, now.AddDays(-1), now.AddMonths(1),
            SubscriptionStatus.Active);

        context.Entitlements.Add(new Entitlement
        {
            Id = Guid.CreateVersion7(),
            UserId = _member.UserId,
            Scope = EntitlementScope.AllMembershipCourses,
            Source = EntitlementSource.Subscription,
            SubscriptionId = subscriptionId,
            Status = EntitlementStatus.Active,
            StartsAtUtc = now.AddDays(-1),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        await context.SaveChangesAsync();

        _firstLessonId = first.Id;
        _secondLessonId = second.Id;
    }
}
