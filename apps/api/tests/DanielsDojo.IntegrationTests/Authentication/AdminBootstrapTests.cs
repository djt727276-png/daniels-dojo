using System.Net;
using System.Net.Http.Headers;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.Infrastructure.Persistence.Seeding;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Authentication;

/// <summary>
/// The one-time launch-administrator bootstrap.
/// </summary>
/// <remarks>
/// The designated email is configuration (<c>Authentication:BootstrapAdminEmail</c>, set to
/// <c>bootstrap.admin@example.test</c> by the test host). Every case below is a way the
/// bootstrap could be abused or fumbled: a forged unverified email, a casing variant, a
/// second identity arriving after consumption, and the ordinary customer who must never be
/// anything but Student.
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class AdminBootstrapTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private const string BootstrapEmail = "bootstrap.admin@example.test";

    private ApiHarness _harness = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _harness = ApiHarness.Create(fixture);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task TheDesignatedEmailBecomesAdminOnItsFirstVerifiedLogin()
    {
        string subject = Guid.NewGuid().ToString();
        using HttpClient client = Client(_harness.Tokens.CreateToken(
            objectId: subject, email: BootstrapEmail, emailVerified: true));

        using HttpResponseMessage session = await client.GetAsync(
            new Uri("/api/v1/auth/session", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);

        // The role is real server-side authorization, not a claim echo.
        using HttpResponseMessage admin = await client.GetAsync(
            new Uri("/api/v1/admin/session", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);

        await using DanielsDojoDbContext context = fixture.CreateContext();

        User user = await context.Users.SingleAsync(u => u.ExternalSubjectId == subject);

        // Bound to the immutable subject: the role row hangs off the local user whose
        // identity columns are the (issuer, subject) pair.
        Assert.True(await context.UserRoles.AnyAsync(
            r => r.UserId == user.Id && r.RoleId == SeedIds.AdminRole));

        // Audited.
        Assert.True(await context.AuditLogs.AnyAsync(
            log => log.Action == "Identity.AdminRoleGranted"
                && log.TargetId == user.Id.ToString()));
    }

    [Fact]
    public async Task ACasingVariantOfTheDesignatedEmailStillMatches()
    {
        using HttpClient client = Client(_harness.Tokens.CreateToken(
            objectId: Guid.NewGuid().ToString(),
            email: "BOOTSTRAP.Admin@EXAMPLE.test",
            emailVerified: true));

        await client.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative));

        using HttpResponseMessage admin = await client.GetAsync(
            new Uri("/api/v1/admin/session", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    [Fact]
    public async Task AnUnverifiedEmailCannotClaimTheBootstrap()
    {
        string subject = Guid.NewGuid().ToString();
        using HttpClient client = Client(_harness.Tokens.CreateToken(
            objectId: subject, email: BootstrapEmail, emailVerified: false));

        using HttpResponseMessage session = await client.GetAsync(
            new Uri("/api/v1/auth/session", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);

        // Signed in as an ordinary customer — and the admin door stays shut.
        using HttpResponseMessage admin = await client.GetAsync(
            new Uri("/api/v1/admin/session", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
    }

    [Fact]
    public async Task EveryOtherEmailBecomesStudent()
    {
        string subject = Guid.NewGuid().ToString();
        using HttpClient client = Client(_harness.Tokens.CreateToken(
            objectId: subject, email: "ordinary.customer@example.test", emailVerified: true));

        await client.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative));

        await using DanielsDojoDbContext context = fixture.CreateContext();
        User user = await context.Users.SingleAsync(u => u.ExternalSubjectId == subject);

        Guid[] roles = [.. await context.UserRoles
            .Where(r => r.UserId == user.Id).Select(r => r.RoleId).ToArrayAsync()];

        Assert.Equal([SeedIds.StudentRole], roles);
    }

    [Fact]
    public async Task ASecondIdentityCannotConsumeAnAlreadyConsumedBootstrap()
    {
        // The designated account signs in and consumes the bootstrap.
        using (HttpClient first = Client(_harness.Tokens.CreateToken(
            objectId: Guid.NewGuid().ToString(), email: BootstrapEmail, emailVerified: true)))
        {
            await first.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative));
        }

        // A different identity presenting the same address — for example after the address
        // was released and re-registered — is just another student.
        using HttpClient second = Client(_harness.Tokens.CreateToken(
            objectId: Guid.NewGuid().ToString(), email: BootstrapEmail, emailVerified: true));

        await second.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative));

        using HttpResponseMessage admin = await second.GetAsync(
            new Uri("/api/v1/admin/session", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);

        await using DanielsDojoDbContext context = fixture.CreateContext();

        // Exactly one Admin assignment exists, ever.
        Assert.Equal(1, await context.UserRoles.CountAsync(r => r.RoleId == SeedIds.AdminRole));
    }

    [Fact]
    public async Task ChangingTheAdminAccountEmailDoesNotMoveTheRole()
    {
        string subject = Guid.NewGuid().ToString();

        using (HttpClient signup = Client(_harness.Tokens.CreateToken(
            objectId: subject, email: BootstrapEmail, emailVerified: true)))
        {
            await signup.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative));
        }

        // The same subject returns with a different address. The role follows the subject.
        using HttpClient renamed = Client(_harness.Tokens.CreateToken(
            objectId: subject, email: "new.address@example.test", emailVerified: true));

        using HttpResponseMessage admin = await renamed.GetAsync(
            new Uri("/api/v1/admin/session", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);

        // And a different subject now holding the old bootstrap address gets nothing.
        using HttpClient impostor = Client(_harness.Tokens.CreateToken(
            objectId: Guid.NewGuid().ToString(), email: BootstrapEmail, emailVerified: true));

        await impostor.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative));

        using HttpResponseMessage refused = await impostor.GetAsync(
            new Uri("/api/v1/admin/session", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task OrdinaryStudentsReceive403FromAdminEndpoints()
    {
        TestActor student = await _harness.SignInAsync();
        using HttpClient client = _harness.CreateClient(student);

        foreach (string path in new[]
        {
            "/api/v1/admin/session",
            "/api/v1/admin/catalog/courses",
            "/api/v1/admin/pricing/offers",
        })
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    private HttpClient Client(string token)
    {
        HttpClient client = _harness.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
