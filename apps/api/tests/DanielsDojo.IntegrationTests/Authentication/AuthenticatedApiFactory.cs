using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DanielsDojo.IntegrationTests.Authentication;

/// <summary>
/// Boots the real API against the container database with authentication enabled, trusting the
/// local test signing key instead of an Entra tenant.
/// </summary>
/// <remarks>
/// This substitutes only the signing material and issuer. The genuine
/// <c>JwtBearerHandler</c>, the scope and authorized-party checks, the provisioning middleware,
/// and the database-backed authorization policies all run exactly as they do in production —
/// there is no fake authentication handler anywhere in these tests.
/// <para>
/// Settings arrive through environment variables because Program.cs reads configuration while
/// the top-level statements run, which is before WebApplicationFactory's configuration
/// callbacks execute. The database suite runs without parallelisation, so mutating process
/// environment state here is safe.
/// </para>
/// </remarks>
public sealed class AuthenticatedApiFactory : WebApplicationFactory<Program>
{
    private static readonly string[] VariableNames =
    [
        "ConnectionStrings__DanielsDojoDatabase",
        "Authentication__EntraExternalId__Enabled",
        "Authentication__EntraExternalId__Authority",
        "Authentication__EntraExternalId__TenantId",
        "Authentication__EntraExternalId__ApiClientId",
        "Authentication__EntraExternalId__RequiredScope",
        "Authentication__EntraExternalId__AllowedClientIds__0",
        "Authentication__EntraExternalId__EmailClaimName",
        "Authentication__EntraExternalId__AllowedCorsOrigin",
        "Media__Storage__Mode",
        "Media__Video__Mode",
        "Commerce__Stripe__Mode",
        "Authentication__BootstrapAdminEmail",
    ];

    private readonly Dictionary<string, string?> _previousValues = [];
    private readonly TestTokenIssuer _tokenIssuer;

    public AuthenticatedApiFactory(string connectionString, TestTokenIssuer tokenIssuer)
    {
        _tokenIssuer = tokenIssuer;

        foreach (string name in VariableNames)
        {
            _previousValues[name] = Environment.GetEnvironmentVariable(name);
        }

        Set("ConnectionStrings__DanielsDojoDatabase", connectionString);
        Set("Authentication__EntraExternalId__Enabled", "true");
        Set("Authentication__EntraExternalId__Authority", TestTokenIssuer.Issuer);
        Set("Authentication__EntraExternalId__TenantId", TestTokenIssuer.TenantId);
        Set("Authentication__EntraExternalId__ApiClientId", TestTokenIssuer.ApiClientId);
        Set("Authentication__EntraExternalId__RequiredScope", TestTokenIssuer.RequiredScope);
        Set("Authentication__EntraExternalId__AllowedClientIds__0", TestTokenIssuer.SpaClientId);
        Set("Authentication__EntraExternalId__EmailClaimName", TestTokenIssuer.EmailClaim);
        Set("Authentication__EntraExternalId__AllowedCorsOrigin", "http://localhost:4200");

        // Deterministic media in every suite. The whole pipeline — authorise, upload, verify,
        // ingest, notify, play — runs in process against real code paths, so no test needs a
        // network, a credential, or a cloud account.
        Set("Media__Storage__Mode", "Deterministic");
        Set("Media__Video__Mode", "Deterministic");
        Set("Commerce__Stripe__Mode", "Deterministic");

        // The one-time launch-administrator bootstrap, exercised by AdminBootstrapTests. The
        // address matches no ordinary test actor, so every other suite is unaffected.
        Set("Authentication__BootstrapAdminEmail", "bootstrap.admin@example.test");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        builder.ConfigureTestServices(services =>
        {
            // Point the real handler at the local key and issuer. Metadata discovery is replaced
            // by a static configuration so no network call is ever made; every other validation
            // rule stays exactly as configured in production.
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    OpenIdConnectConfiguration configuration = new();
                    configuration.SigningKeys.Add(_tokenIssuer.PublicSigningKey);

                    options.Authority = string.Empty;
                    options.MetadataAddress = string.Empty;
                    options.Configuration = configuration;
                    options.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);

                    options.TokenValidationParameters.ValidIssuer = TestTokenIssuer.Issuer;
                    options.TokenValidationParameters.ValidIssuers = [TestTokenIssuer.Issuer];
                    options.TokenValidationParameters.IssuerSigningKey = _tokenIssuer.PublicSigningKey;
                    options.TokenValidationParameters.IssuerSigningKeys = [_tokenIssuer.PublicSigningKey];

                    // Microsoft.Identity.Web installs an Entra-specific issuer validator that
                    // would reject the local test issuer. Clearing it falls back to the standard
                    // ValidIssuer comparison, which is still a real issuer check.
                    options.TokenValidationParameters.IssuerValidator = null;
                    options.TokenValidationParameters.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                });
        });
    }

    private static void Set(string name, string value) => Environment.SetEnvironmentVariable(name, value);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach ((string name, string? value) in _previousValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        base.Dispose(disposing);
    }
}
