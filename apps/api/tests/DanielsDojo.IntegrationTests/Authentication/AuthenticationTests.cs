using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.Infrastructure.Persistence.Seeding;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Authentication;

/// <summary>
/// Exercises the real bearer pipeline, first-login provisioning, and database-backed
/// authorization against a real SQL Server database.
/// </summary>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class AuthenticationTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private static readonly JsonSerializerOptions SessionJsonOptions = new(JsonSerializerDefaults.Web);

    private TestTokenIssuer _tokens = null!;
    private AuthenticatedApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _tokens = new TestTokenIssuer();
        _factory = new AuthenticatedApiFactory(fixture.ConnectionString, _tokens);
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _factory?.Dispose();
        _tokens?.Dispose();
    }

    // ---------------------------------------------------------------- public endpoints

    [Theory]
    [InlineData("/api/v1/system/status")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task PublicEndpoints_RemainPublic(string path)
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------- missing / bad tokens

    [Theory]
    [InlineData("/api/v1/auth/session")]
    [InlineData("/api/v1/admin/session")]
    public async Task ProtectedEndpoints_WithoutToken_Return401(string path)
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenSignedByAnotherKey_Is401()
    {
        using RSA attackerKey = RSA.Create(2048);
        string token = _tokens.CreateToken(signingKeyOverride: attackerKey);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/api/v1/auth/session", token));
    }

    [Fact]
    public async Task TokenFromAnotherIssuer_Is401()
    {
        string token = _tokens.CreateToken(issuer: "https://evil.example/v2.0");

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/api/v1/auth/session", token));
    }

    [Fact]
    public async Task TokenForAnotherAudience_Is401()
    {
        string token = _tokens.CreateToken(audience: "00000000-0000-0000-0000-0000000000ff");

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/api/v1/auth/session", token));
    }

    [Fact]
    public async Task ExpiredToken_Is401()
    {
        string token = _tokens.CreateToken(
            notBefore: DateTime.UtcNow.AddMinutes(-30),
            expires: DateTime.UtcNow.AddMinutes(-10));

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/api/v1/auth/session", token));
    }

    [Fact]
    public async Task NotYetValidToken_Is401()
    {
        string token = _tokens.CreateToken(
            notBefore: DateTime.UtcNow.AddMinutes(30),
            expires: DateTime.UtcNow.AddMinutes(60));

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/api/v1/auth/session", token));
    }

    // ---------------------------------------------------------------- scope and client checks

    [Fact]
    public async Task TokenWithoutRequiredScope_Is403()
    {
        string token = _tokens.CreateToken(scope: null);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync("/api/v1/auth/session", token));
    }

    [Fact]
    public async Task TokenWithWrongScope_Is403()
    {
        string token = _tokens.CreateToken(scope: "some.other.scope");

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync("/api/v1/auth/session", token));
    }

    [Fact]
    public async Task TokenFromUnlistedClient_Is403()
    {
        // A valid user identity from a client that is not the Daniel's Dojo SPA.
        string token = _tokens.CreateToken(authorizedParty: "00000000-0000-0000-0000-0000000000dd");

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync("/api/v1/auth/session", token));
    }

    [Fact]
    public async Task TokenWithoutAuthorizedParty_Is403()
    {
        string token = _tokens.CreateToken(authorizedParty: null);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync("/api/v1/auth/session", token));
    }

    // ---------------------------------------------------------------- identity claim checks

    [Fact]
    public async Task TokenWithoutObjectId_Is403()
    {
        string token = _tokens.CreateToken(objectId: string.Empty);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync("/api/v1/auth/session", token));
    }

    [Fact]
    public async Task NewCustomerWithoutEmailClaim_Is403_AndIsNotProvisioned()
    {
        string token = _tokens.CreateToken(objectId: NewObjectId(), email: null);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync("/api/v1/auth/session", token));

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Equal(0, await context.Users.CountAsync());
    }

    // ---------------------------------------------------------------- provisioning

    [Fact]
    public async Task ValidToken_ProvisionsOneUserWithExactlyStudent()
    {
        string objectId = NewObjectId();
        string token = _tokens.CreateTokenForUser(objectId, "new@example.test", "New Customer");

        SessionPayload session = await GetSessionAsync(token);

        Assert.Equal("New Customer", session.DisplayName);
        Assert.Equal("new@example.test", session.Email);
        Assert.Equal([ApplicationRolesStudent], session.Roles);

        await using DanielsDojoDbContext context = fixture.CreateContext();

        User user = await context.Users.SingleAsync();
        Assert.Equal(TestTokenIssuer.TenantId, user.ExternalIssuer);
        Assert.Equal(objectId, user.ExternalSubjectId);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(session.UserId, user.Id);

        Assert.Equal(1, await context.UserRoles.CountAsync(role => role.UserId == user.Id));
        Assert.Equal(
            SeedIds.StudentRole,
            (await context.UserRoles.SingleAsync(role => role.UserId == user.Id)).RoleId);
    }

    [Fact]
    public async Task RepeatedRequests_KeepTheSameUserAndDoNotDuplicateRoles()
    {
        string objectId = NewObjectId();
        string token = _tokens.CreateTokenForUser(objectId);

        SessionPayload first = await GetSessionAsync(token);
        SessionPayload second = await GetSessionAsync(token);
        SessionPayload third = await GetSessionAsync(token);

        Assert.Equal(first.UserId, second.UserId);
        Assert.Equal(first.UserId, third.UserId);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Equal(1, await context.Users.CountAsync());
        Assert.Equal(1, await context.UserRoles.CountAsync());
    }

    [Fact]
    public async Task SimultaneousFirstRequests_AreIdempotentAndRaceSafe()
    {
        string objectId = NewObjectId();
        string token = _tokens.CreateTokenForUser(objectId);

        using HttpClient client = _factory.CreateClient();

        // Eight concurrent first requests for the same brand-new identity.
        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => SendAsync(client, "/api/v1/auth/session", token)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        Guid[] userIds = await Task.WhenAll(responses.Select(async response =>
            (await ReadSessionAsync(response)).UserId));

        Assert.Single(userIds.Distinct());

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Equal(1, await context.Users.CountAsync());
        Assert.Equal(1, await context.UserRoles.CountAsync());

        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }
    }

    // ---------------------------------------------------------------- profile synchronisation

    [Fact]
    public async Task ChangedEmailAndName_UpdateProfileWithoutChangingIdentityOrRoles()
    {
        string objectId = NewObjectId();

        SessionPayload before = await GetSessionAsync(
            _tokens.CreateTokenForUser(objectId, "before@example.test", "Before Name"));

        SessionPayload after = await GetSessionAsync(
            _tokens.CreateTokenForUser(objectId, "after@example.test", "After Name"));

        Assert.Equal(before.UserId, after.UserId);
        Assert.Equal("after@example.test", after.Email);
        Assert.Equal("After Name", after.DisplayName);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        User user = await context.Users.SingleAsync();

        Assert.Equal(objectId, user.ExternalSubjectId);
        Assert.Equal("AFTER@EXAMPLE.TEST", user.NormalizedEmail);
        Assert.Equal(1, await context.UserRoles.CountAsync());
    }

    [Fact]
    public async Task ExistingAdmin_KeepsAdminDuringSynchronisation()
    {
        string objectId = NewObjectId();
        SessionPayload initial = await GetSessionAsync(_tokens.CreateTokenForUser(objectId));

        await GrantAdminAsync(initial.UserId);

        // A later sign-in with changed profile data must not disturb the role set.
        SessionPayload after = await GetSessionAsync(
            _tokens.CreateTokenForUser(objectId, "renamed@example.test", "Renamed"));

        Assert.Equal(initial.UserId, after.UserId);
        Assert.Contains(ApplicationRolesAdmin, after.Roles);
        Assert.Contains(ApplicationRolesStudent, after.Roles);
    }

    [Fact]
    public async Task EmailCollision_CannotTakeOverAnotherExternalIdentity()
    {
        const string SharedEmail = "shared@example.test";

        string firstObjectId = NewObjectId();
        string secondObjectId = NewObjectId();

        SessionPayload first = await GetSessionAsync(
            _tokens.CreateTokenForUser(firstObjectId, SharedEmail, "First Person"));

        SessionPayload second = await GetSessionAsync(
            _tokens.CreateTokenForUser(secondObjectId, SharedEmail, "Second Person"));

        // Same email, different immutable identity: two separate accounts, no takeover.
        Assert.NotEqual(first.UserId, second.UserId);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Equal(2, await context.Users.CountAsync());
    }

    // ---------------------------------------------------------------- account state and roles

    [Fact]
    public async Task DisabledUser_Is403()
    {
        string objectId = NewObjectId();
        string token = _tokens.CreateTokenForUser(objectId);

        SessionPayload session = await GetSessionAsync(token);

        await using (DanielsDojoDbContext context = fixture.CreateContext())
        {
            User user = await context.Users.SingleAsync(candidate => candidate.Id == session.UserId);
            user.Status = UserStatus.Disabled;
            await context.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync("/api/v1/auth/session", token));
    }

    [Fact]
    public async Task Student_IsForbiddenOnAdminEndpoint()
    {
        string token = _tokens.CreateTokenForUser(NewObjectId());

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("/api/v1/auth/session", token));
        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync("/api/v1/admin/session", token));
    }

    [Fact]
    public async Task Admin_IsAllowedOnAdminEndpoint()
    {
        string objectId = NewObjectId();
        string token = _tokens.CreateTokenForUser(objectId);

        SessionPayload session = await GetSessionAsync(token);
        await GrantAdminAsync(session.UserId);

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("/api/v1/admin/session", token));
    }

    // ---------------------------------------------------------------- leakage

    [Fact]
    public async Task ProblemDetails_DoNotLeakTokenOrClaimMaterial()
    {
        string token = _tokens.CreateToken(scope: "wrong.scope");

        using HttpClient client = _factory.CreateClient();
        using HttpResponseMessage response = await SendAsync(client, "/api/v1/auth/session", token);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(token, body, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scp", body, StringComparison.Ordinal);
        Assert.DoesNotContain("oid", body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestTokenIssuer.TenantId, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provisioning_NeverStoresRawTokenMaterial()
    {
        string objectId = NewObjectId();
        string token = _tokens.CreateTokenForUser(objectId);

        await GetSessionAsync(token);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        User user = await context.Users.SingleAsync();

        // The JWT is three base64url segments joined by dots; no column may contain any of it.
        string firstSegment = token.Split('.')[0];

        Assert.DoesNotContain(firstSegment, user.Email, StringComparison.Ordinal);
        Assert.DoesNotContain(firstSegment, user.DisplayName, StringComparison.Ordinal);
        Assert.DoesNotContain(firstSegment, user.ExternalSubjectId, StringComparison.Ordinal);
        Assert.DoesNotContain(firstSegment, user.ExternalIssuer, StringComparison.Ordinal);
        Assert.DoesNotContain(".", user.ExternalSubjectId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionResponse_ExposesOnlySafeFields()
    {
        string objectId = NewObjectId();

        // The email deliberately does not embed the object ID, so the assertions below prove
        // the response omits the external identity rather than merely echoing it inside another
        // field.
        string token = _tokens.CreateTokenForUser(objectId, "safe.fields@example.test", "Safe Fields");

        using HttpClient client = _factory.CreateClient();
        using HttpResponseMessage response = await SendAsync(client, "/api/v1/auth/session", token);
        string body = await response.Content.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(body);
        string[] properties = [.. document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)];

        Assert.Equal(["displayName", "email", "roles", "userId"], properties);

        // The external identity must never reach the client.
        Assert.DoesNotContain(objectId, body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestTokenIssuer.TenantId, body, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private const string ApplicationRolesStudent = "Student";
    private const string ApplicationRolesAdmin = "Admin";

    private static string NewObjectId() => Guid.NewGuid().ToString();

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string token)
    {
        HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private async Task<HttpStatusCode> GetStatusAsync(string path, string token)
    {
        using HttpClient client = _factory.CreateClient();
        using HttpResponseMessage response = await SendAsync(client, path, token);
        return response.StatusCode;
    }

    private async Task<SessionPayload> GetSessionAsync(string token)
    {
        using HttpClient client = _factory.CreateClient();
        using HttpResponseMessage response = await SendAsync(client, "/api/v1/auth/session", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadSessionAsync(response);
    }

    private static async Task<SessionPayload> ReadSessionAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SessionPayload>(body, SessionJsonOptions)!;
    }

    private async Task GrantAdminAsync(Guid userId)
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        Infrastructure.Identity.AdminRoleGrantService service = new(context, TimeProvider.System);

        AdminGrantResultAssertion.AssertSucceeded(
            await service.GrantAsync(userId, "Test fixture grant.", "test@fixture", Guid.NewGuid().ToString("N")));
    }

    private sealed record SessionPayload(
        Guid UserId,
        string DisplayName,
        string Email,
        IReadOnlyList<string> Roles);
}

/// <summary>Small assertion helper keeping the grant call sites readable.</summary>
internal static class AdminGrantResultAssertion
{
    public static void AssertSucceeded(Infrastructure.Identity.AdminGrantResult result)
        => Assert.True(result.Succeeded, $"Admin grant failed: {result.Failure}.");
}
