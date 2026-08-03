using System.Net;
using DanielsDojo.Api.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace DanielsDojo.IntegrationTests.Authentication;

/// <summary>
/// Locks the configuration safety properties: disabled mode never exposes a protected
/// endpoint, enabling authentication with bad configuration refuses to start, and Production
/// cannot silently run without authentication at all.
/// </summary>
public sealed class AuthenticationConfigurationTests
{
    // ------------------------------------------------- disabled mode is still not public

    [Theory]
    [InlineData("/api/v1/auth/session")]
    [InlineData("/api/v1/admin/session")]
    public async Task DisabledAuthentication_StillRejectsProtectedEndpoints(string path)
    {
        // The committed default is Enabled=false. Protected endpoints must answer 401, not 200:
        // disabled mode registers a scheme with no signing keys, issuers, or audiences, so
        // nothing can ever authenticate — it does not remove the authorization requirement.
        using DanielsDojoApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/system/status")]
    [InlineData("/health/live")]
    public async Task DisabledAuthentication_KeepsPublicEndpointsPublic(string path)
    {
        using DanielsDojoApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DisabledAuthentication_RejectsAnyBearerToken()
    {
        using DanielsDojoApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/auth/session");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer aaa.bbb.ccc");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------- validator rules

    [Fact]
    public void Production_WithAuthenticationDisabled_FailsValidation()
    {
        ValidateOptionsResult result = Validate(Environments.Production, new EntraExternalIdOptions
        {
            Enabled = false,
        });

        Assert.True(result.Failed);
        Assert.Contains("Production", result.FailureMessage!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void NonProduction_WithAuthenticationDisabled_IsAllowed(string environmentName)
    {
        ValidateOptionsResult result = Validate(environmentName, new EntraExternalIdOptions
        {
            Enabled = false,
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Enabled_WithCompleteConfiguration_Succeeds()
    {
        ValidateOptionsResult result = Validate(Environments.Production, Valid());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Enabled_WithEmptyConfiguration_FailsInEveryEnvironment()
    {
        // Validation is not conditional on Development: an incomplete configuration is refused
        // wherever it appears.
        foreach (string environmentName in
                 new[] { Environments.Development, Environments.Staging, Environments.Production })
        {
            ValidateOptionsResult result = Validate(environmentName, new EntraExternalIdOptions
            {
                Enabled = true,
            });

            Assert.True(result.Failed, $"Expected failure in {environmentName}.");
        }
    }

    [Fact]
    public void Enabled_WithPlaceholderIdentifiers_Fails()
    {
        // Unsubstituted worksheet placeholders must never be mistaken for real identifiers.
        EntraExternalIdOptions options = Valid();
        options.TenantId = "[EXTERNAL_TENANT_ID]";
        options.ApiClientId = "[API_CLIENT_ID]";

        ValidateOptionsResult result = Validate(Environments.Production, options);

        Assert.True(result.Failed);
        Assert.Contains("must be a GUID", result.FailureMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_WithMalformedAuthority_Fails()
    {
        EntraExternalIdOptions options = Valid();
        options.Authority = "not-a-uri";

        Assert.True(Validate(Environments.Production, options).Failed);
    }

    [Fact]
    public void Enabled_WithAuthorityForAnotherTenant_Fails()
    {
        // The authority must address the same tenant whose tokens are accepted.
        EntraExternalIdOptions options = Valid();
        options.Authority = "https://other.ciamlogin.com/99999999-9999-4999-8999-999999999999/v2.0";

        ValidateOptionsResult result = Validate(Environments.Production, options);

        Assert.True(result.Failed);
        Assert.Contains("does not contain", result.FailureMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_WithEmptyClientAllowlist_Fails()
    {
        // Without an azp allowlist any client holding a user identity could call the API.
        EntraExternalIdOptions options = Valid();
        options.AllowedClientIds.Clear();

        ValidateOptionsResult result = Validate(Environments.Production, options);

        Assert.True(result.Failed);
        Assert.Contains("AllowedClientIds", result.FailureMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureMessages_NameTheKeyWithoutEchoingItsValue()
    {
        EntraExternalIdOptions options = Valid();
        options.TenantId = "super-secret-looking-value";

        ValidateOptionsResult result = Validate(Environments.Production, options);

        Assert.True(result.Failed);
        Assert.Contains("TenantId", result.FailureMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "super-secret-looking-value",
            result.FailureMessage!,
            StringComparison.Ordinal);
    }

    // ------------------------------------------------- helpers

    private const string TenantId = "11111111-1111-4111-8111-111111111111";

    private static EntraExternalIdOptions Valid()
    {
        EntraExternalIdOptions options = new()
        {
            Enabled = true,
            Authority = $"https://danielsdojo.ciamlogin.com/{TenantId}/v2.0",
            TenantId = TenantId,
            ApiClientId = "22222222-2222-4222-8222-222222222222",
            RequiredScope = "access_as_user",
            EmailClaimName = "email",
            AllowedCorsOrigin = "http://localhost:4200",
        };

        options.AllowedClientIds.Add("33333333-3333-4333-8333-333333333333");
        return options;
    }

    private static ValidateOptionsResult Validate(string environmentName, EntraExternalIdOptions options)
    {
        EntraExternalIdOptionsValidator validator = new(new StubHostEnvironment(environmentName));
        return validator.Validate(Options.DefaultName, options);
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "DanielsDojo.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
