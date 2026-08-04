using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DanielsDojo.Infrastructure.Identity;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Authentication;
using DanielsDojo.IntegrationTests.Database;
using Xunit;

namespace DanielsDojo.IntegrationTests;

/// <summary>
/// Signs test actors in through the real bearer pipeline and hands back ready-to-use clients.
/// </summary>
/// <remarks>
/// Every actor here is provisioned the way a real customer is: a locally signed token reaches
/// the genuine JWT handler, the provisioning middleware creates the local user, and Admin is
/// granted only through the same audited service the operator command uses. No test fakes an
/// identity or writes a role row directly.
/// </remarks>
internal sealed class ApiHarness : IAsyncDisposable
{
    /// <summary>Web-defaults JSON options matching the API's own serialisation.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SqlServerDatabaseFixture _fixture;

    private ApiHarness(SqlServerDatabaseFixture fixture, TestTokenIssuer tokens, AuthenticatedApiFactory factory)
    {
        _fixture = fixture;
        Tokens = tokens;
        Factory = factory;
    }

    /// <summary>The local signing authority the API is configured to trust.</summary>
    public TestTokenIssuer Tokens { get; }

    /// <summary>The API host under test.</summary>
    public AuthenticatedApiFactory Factory { get; }

    /// <summary>Starts a host bound to the container database.</summary>
    public static ApiHarness Create(SqlServerDatabaseFixture fixture)
    {
        TestTokenIssuer tokens = new();

        return new ApiHarness(fixture, tokens, new AuthenticatedApiFactory(fixture.ConnectionString, tokens));
    }

    /// <summary>
    /// Signs an actor in for the first time, which provisions the local user, then optionally
    /// grants Admin through the audited grant service.
    /// </summary>
    public async Task<TestActor> SignInAsync(bool admin = false, string? objectId = null)
    {
        string subject = objectId ?? Guid.NewGuid().ToString();
        string token = Tokens.CreateTokenForUser(subject);

        using HttpClient client = CreateClient(token);
        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative));

        Assert.True(response.IsSuccessStatusCode, $"Sign-in failed: {response.StatusCode}.");

        SessionPayload session = (await response.Content.ReadFromJsonAsync<SessionPayload>(Json))!;

        if (admin)
        {
            await using DanielsDojoDbContext context = _fixture.CreateContext();
            AdminRoleGrantService grants = new(context, TimeProvider.System);
            AdminGrantResult result = await grants.GrantAsync(
                session.UserId,
                "Integration test fixture grant.",
                "test@fixture",
                Guid.NewGuid().ToString("N"));

            Assert.True(result.Succeeded, $"Admin grant failed: {result.Failure}.");
        }

        return new TestActor(session.UserId, token);
    }

    /// <summary>Creates a client that presents the actor's bearer token on every request.</summary>
    public HttpClient CreateClient(TestActor actor) => CreateClient(actor.Token);

    /// <summary>Creates a client presenting the supplied token.</summary>
    public HttpClient CreateClient(string token)
    {
        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Factory.Dispose();
        Tokens.Dispose();

        return ValueTask.CompletedTask;
    }

    private sealed record SessionPayload(Guid UserId, string DisplayName, string Email, IReadOnlyList<string> Roles);
}

/// <summary>A signed-in test actor: the local user identifier plus its bearer token.</summary>
internal sealed record TestActor(Guid UserId, string Token);

/// <summary>Small HTTP helpers that keep the endpoint suites readable.</summary>
internal static class HarnessExtensions
{
    /// <summary>Sends JSON and returns the parsed body, asserting the expected status.</summary>
    public static async Task<JsonDocument> SendJsonAsync(
        this HttpClient client,
        HttpMethod method,
        string path,
        object? payload,
        System.Net.HttpStatusCode expected)
    {
        using HttpResponseMessage response = await client.SendJsonAsync(method, path, payload);
        string body = await response.Content.ReadAsStringAsync();

        Assert.True(
            expected == response.StatusCode,
            $"{method} {path} expected {expected} but returned {response.StatusCode}: {body}");

        return string.IsNullOrWhiteSpace(body)
            ? JsonDocument.Parse("null")
            : JsonDocument.Parse(body);
    }

    /// <summary>Sends JSON and returns the raw response for status-only assertions.</summary>
    public static Task<HttpResponseMessage> SendJsonAsync(
        this HttpClient client,
        HttpMethod method,
        string path,
        object? payload)
    {
        HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative));

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: ApiHarness.Json);
        }

        return client.SendAsync(request);
    }

    /// <summary>Gets JSON, asserting a 200 response.</summary>
    public static async Task<JsonDocument> GetJsonAsync(this HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.IsSuccessStatusCode,
            $"GET {path} returned {response.StatusCode}: {body}");

        return JsonDocument.Parse(body);
    }

    /// <summary>Reads the stable machine-readable error code from a problem response.</summary>
    public static string? ProblemCode(this JsonDocument problem) =>
        problem.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
}
