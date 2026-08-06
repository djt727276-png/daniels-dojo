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
/// Regression coverage for the production admin-bootstrap failure of August 2026.
/// </summary>
/// <remarks>
/// Entra External ID email-OTP user flows verify the address at sign-up but never emit an
/// <c>email_verified</c> claim, so every earlier test that passed the claim explicitly was
/// modelling a token shape production never sees. Without
/// <c>EmailVerifiedByUserFlow</c>, the designated administrator provisioned as an ordinary
/// Student and the bootstrap silently never ran. These tests pin both sides: the opted-in
/// tenant bootstraps on a claim-less token, and the default configuration still refuses it.
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class EmailVerifiedByUserFlowTests(SqlServerDatabaseFixture fixture)
    : IAsyncLifetime, IDisposable
{
    private const string BootstrapEmail = "bootstrap.admin@example.test";

    private TestTokenIssuer _tokens = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _tokens = new TestTokenIssuer();
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => _tokens?.Dispose();

    [Fact]
    public async Task AFlowVerifiedTenantBootstrapsWithoutAVerificationClaim()
    {
        using AuthenticatedApiFactory factory =
            new(fixture.ConnectionString, _tokens, emailVerifiedByUserFlow: true);

        string subject = Guid.NewGuid().ToString();
        using HttpClient client = Client(factory, _tokens.CreateToken(
            objectId: subject, email: BootstrapEmail, emailVerified: null));

        using HttpResponseMessage session = await client.GetAsync(
            new Uri("/api/v1/auth/session", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);

        using HttpResponseMessage admin = await client.GetAsync(
            new Uri("/api/v1/admin/session", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        User user = await context.Users.SingleAsync(u => u.ExternalSubjectId == subject);

        Assert.True(user.EmailVerified);
        Assert.True(await context.UserRoles.AnyAsync(
            r => r.UserId == user.Id && r.RoleId == SeedIds.AdminRole));
        Assert.True(await context.AuditLogs.AnyAsync(
            log => log.Action == "Identity.AdminRoleGranted"
                && log.TargetId == user.Id.ToString()));
    }

    [Fact]
    public async Task WithoutTheOptInAClaimlessTokenRemainsUnverifiedAndStudent()
    {
        using AuthenticatedApiFactory factory = new(fixture.ConnectionString, _tokens);

        string subject = Guid.NewGuid().ToString();
        using HttpClient client = Client(factory, _tokens.CreateToken(
            objectId: subject, email: BootstrapEmail, emailVerified: null));

        using HttpResponseMessage session = await client.GetAsync(
            new Uri("/api/v1/auth/session", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);

        using HttpResponseMessage admin = await client.GetAsync(
            new Uri("/api/v1/admin/session", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        User user = await context.Users.SingleAsync(u => u.ExternalSubjectId == subject);

        Assert.False(user.EmailVerified);
        Assert.False(await context.UserRoles.AnyAsync(r => r.RoleId == SeedIds.AdminRole));
    }

    [Fact]
    public async Task AnExplicitlyUnverifiedClaimOverridesTheOptIn()
    {
        using AuthenticatedApiFactory factory =
            new(fixture.ConnectionString, _tokens, emailVerifiedByUserFlow: true);

        using HttpClient client = Client(factory, _tokens.CreateToken(
            objectId: Guid.NewGuid().ToString(), email: BootstrapEmail, emailVerified: false));

        await client.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative));

        using HttpResponseMessage admin = await client.GetAsync(
            new Uri("/api/v1/admin/session", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
    }

    [Fact]
    public async Task TheOptInDoesNotInventVerificationWithoutAnAddress()
    {
        using AuthenticatedApiFactory factory =
            new(fixture.ConnectionString, _tokens, emailVerifiedByUserFlow: true);

        using HttpClient client = Client(factory, _tokens.CreateToken(
            objectId: Guid.NewGuid().ToString(), email: null, emailVerified: null));

        // No address at all still fails provisioning outright; the flag verifies an
        // address the flow proved, it does not conjure one.
        using HttpResponseMessage session = await client.GetAsync(
            new Uri("/api/v1/auth/session", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, session.StatusCode);
    }

    private static HttpClient Client(AuthenticatedApiFactory factory, string token)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
