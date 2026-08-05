using DanielsDojo.Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;

namespace DanielsDojo.Api.Authentication;

/// <summary>Wires Entra External ID bearer authentication and application authorization.</summary>
public static class AuthenticationRegistration
{
    /// <summary>Policy requiring the local Student role.</summary>
    public const string StudentPolicy = "RequireStudent";

    /// <summary>Policy requiring the local Admin role.</summary>
    public const string AdminPolicy = "RequireAdmin";

    /// <summary>Named CORS policy for the configured Angular origin.</summary>
    public const string CorsPolicy = "DanielsDojoSpa";

    /// <summary>Scheme that routes a request to the Entra or Development handler.</summary>
    private const string SelectorScheme = "DanielsDojoScheme";

    /// <summary>
    /// Chooses the handler for a request by reading the bearer token's issuer.
    /// </summary>
    /// <remarks>
    /// This only routes; it never accepts anything. The selected handler still performs full
    /// signature, issuer, audience, and lifetime validation, so a token that merely claims the
    /// Development issuer is rejected unless it was signed by this process's key.
    /// </remarks>
    private static string SelectSchemeForRequest(HttpContext context)
    {
        string? header = context.Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(header)
            || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return JwtBearerDefaults.AuthenticationScheme;
        }

        string token = header["Bearer ".Length..].Trim();

        try
        {
            Microsoft.IdentityModel.JsonWebTokens.JsonWebToken parsed =
                new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler()
                    .ReadJsonWebToken(token);

            return string.Equals(parsed.Issuer, DevelopmentAuthOptions.Issuer, StringComparison.Ordinal)
                ? DevelopmentAuthOptions.SchemeName
                : JwtBearerDefaults.AuthenticationScheme;
        }
        catch (ArgumentException)
        {
            // Unparseable token: hand it to the Entra handler, which rejects it as usual.
            return JwtBearerDefaults.AuthenticationScheme;
        }
    }

    /// <summary>
    /// Registers configuration, bearer validation, and role policies. Configuration is validated
    /// on start so a misconfigured deployment fails immediately rather than at the first
    /// sign-in attempt.
    /// </summary>
    public static IServiceCollection AddDanielsDojoAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddOptions<EntraExternalIdOptions>()
            .Bind(configuration.GetSection(EntraExternalIdOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<EntraExternalIdOptions>>(
            provider => new EntraExternalIdOptionsValidator(
                provider.GetRequiredService<IHostEnvironment>()));

        services.AddOptions<DevelopmentAuthOptions>()
            .Bind(configuration.GetSection(DevelopmentAuthOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<DevelopmentAuthOptions>>(
            provider => new DevelopmentAuthOptionsValidator(
                provider.GetRequiredService<IHostEnvironment>()));

        EntraExternalIdOptions settings = new();
        configuration.GetSection(EntraExternalIdOptions.SectionName).Bind(settings);

        DevelopmentAuthOptions developmentSettings = new();
        configuration.GetSection(DevelopmentAuthOptions.SectionName).Bind(developmentSettings);

        // Two independent conditions. The environment check is not configurable, so no
        // setting can switch the harness on outside Development.
        bool developmentAuthActive =
            DevelopmentAuthOptions.IsExactlyDevelopment(environment) && developmentSettings.Enabled;

        // With the harness active the default scheme is a selector: the local-user resolution
        // middleware runs before authorization and needs an authenticated principal, so the
        // right handler has to be chosen during UseAuthentication rather than later.
        AuthenticationBuilder authentication = services.AddAuthentication(
            developmentAuthActive ? SelectorScheme : JwtBearerDefaults.AuthenticationScheme);

        if (developmentAuthActive)
        {
            authentication.AddPolicyScheme(SelectorScheme, SelectorScheme, policyOptions =>
                policyOptions.ForwardDefaultSelector = SelectSchemeForRequest);
        }

        if (developmentAuthActive)
        {
            // One signing key per process, generated in memory and never persisted, so a
            // restart invalidates every previously issued Development token.
            services.AddSingleton<DevelopmentSigningKey>();

            authentication.AddJwtBearer(DevelopmentAuthOptions.SchemeName, jwtOptions =>
            {
                jwtOptions.RequireHttpsMetadata = false;
                jwtOptions.MapInboundClaims = false;
                jwtOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ValidateIssuer = true,
                    ValidIssuer = DevelopmentAuthOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = DevelopmentAuthOptions.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                    RoleClaimType = "__unused_role_claim__",
                };
            });

            // The signing key is only known once the container is built, so it is attached
            // to the scheme afterwards.
            services.AddOptions<JwtBearerOptions>(DevelopmentAuthOptions.SchemeName)
                .Configure<DevelopmentSigningKey>((jwtOptions, signingKey) =>
                {
                    jwtOptions.TokenValidationParameters.IssuerSigningKey = signingKey.PublicKey;
                    jwtOptions.TokenValidationParameters.IssuerSigningKeys = [signingKey.PublicKey];
                });
        }

        if (settings.Enabled)
        {
            authentication.AddMicrosoftIdentityWebApi(
                jwtOptions => ConfigureJwtBearer(jwtOptions, settings),
                identityOptions =>
                {
                    identityOptions.Instance = settings.Authority;
                    identityOptions.TenantId = settings.TenantId;
                    identityOptions.ClientId = settings.ApiClientId;
                },
                JwtBearerDefaults.AuthenticationScheme,
                subscribeToJwtBearerMiddlewareDiagnosticsEvents: false);
        }
        else
        {
            // No identity provider is configured for this host. The scheme is still registered
            // so protected endpoints answer 401 rather than 500, and public routes keep working,
            // but nothing can ever validate: there are no signing keys, issuers, or audiences.
            // Metadata discovery is never attempted, so the host boots without a tenant.
            authentication.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, jwtOptions =>
            {
                jwtOptions.RequireHttpsMetadata = false;
                jwtOptions.MapInboundClaims = false;
                jwtOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    IssuerSigningKeys = [],
                    ValidIssuers = [],
                    ValidAudiences = [],
                };
            });
        }

        // Registered after AddMicrosoftIdentityWebApi so this post-configure runs last.
        //
        // Microsoft.Identity.Web installs an OnTokenValidated handler that rejects a token
        // carrying neither 'scp' nor 'roles' by failing authentication, which surfaces as 401.
        // For this API a validly authenticated caller that lacks the delegated scope is an
        // authorization failure, not an authentication one, so it must be 403 — and the answer
        // must not differ between "no scope claim" and "wrong scope value", or the difference
        // becomes a probing signal. Clearing the handler leaves the single scope decision to
        // the provisioning middleware.
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            jwtOptions =>
            {
                jwtOptions.Events ??= new JwtBearerEvents();
                jwtOptions.Events.OnTokenValidated = static _ => Task.CompletedTask;

                // Browsers cannot attach an Authorization header to a WebSocket handshake, so
                // SignalR sends the bearer as the access_token query parameter. Accepted for
                // hub paths only — everything else keeps the header requirement — and the
                // token is validated by exactly the same pipeline either way.
                jwtOptions.Events.OnMessageReceived = static messageContext =>
                {
                    string? accessToken = messageContext.Request.Query["access_token"];

                    if (!string.IsNullOrEmpty(accessToken)
                        && messageContext.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        messageContext.Token = accessToken;
                    }

                    return Task.CompletedTask;
                };

                // Suppress the default WWW-Authenticate detail so a rejected token never
                // explains which validation step failed.
                jwtOptions.Events.OnChallenge = static challengeContext =>
                {
                    challengeContext.HandleResponse();
                    challengeContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
            });

        if (developmentAuthActive)
        {
            // The harness mints tokens whose authorized party is its own fixed client. Adding
            // it to the allowlist here — rather than in configuration — means the value cannot
            // be introduced by a settings file in any other environment.
            services.PostConfigure<EntraExternalIdOptions>(entraOptions =>
            {
                if (!entraOptions.AllowedClientIds.Contains(DevelopmentAuthOptions.ClientId))
                {
                    entraOptions.AllowedClientIds.Add(DevelopmentAuthOptions.ClientId);
                }
            });
        }

        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<CurrentUserAccessor>());
        services.AddScoped<IAuthorizationHandler, ApplicationRoleHandler>();

        // Both schemes feed the same policies, so authorization is expressed once regardless of
        // how the caller authenticated. The Development scheme is only listed when it exists.
        // Policies name no scheme, so they evaluate whatever the default scheme authenticated —
        // the selector above when the harness is active, the Entra handler otherwise. The
        // authorization rules themselves are identical either way.
        services.AddAuthorizationBuilder()
            .AddPolicy(StudentPolicy, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new ApplicationRoleRequirement(ApplicationRoles.Student)))
            .AddPolicy(AdminPolicy, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new ApplicationRoleRequirement(ApplicationRoles.Admin)));

        // Exactly one origin, no wildcards. Credentials are not enabled because the SPA
        // authenticates with a bearer header rather than cookies.
        services.AddCors(cors => cors.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(settings.AllowedCorsOrigin)
            .WithHeaders("Authorization", "Content-Type")
            .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")));

        return services;
    }

    private static void ConfigureJwtBearer(JwtBearerOptions jwtOptions, EntraExternalIdOptions settings)
    {
        jwtOptions.Authority = settings.Authority;
        jwtOptions.RequireHttpsMetadata = true;

        // When authentication is disabled no metadata is fetched, so a developer or test host
        // without a tenant still boots. Protected endpoints then reject every caller because no
        // signing key can ever validate.
        if (!settings.Enabled)
        {
            jwtOptions.Authority = string.Empty;
            jwtOptions.MetadataAddress = string.Empty;
        }

        jwtOptions.MapInboundClaims = false;

        jwtOptions.TokenValidationParameters = new TokenValidationParameters
        {
            // Signature and algorithm are enforced by the framework; an unsigned or
            // "alg: none" token can never satisfy this.
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,

            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudiences = settings.ValidAudiences,

            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),

            NameClaimType = "name",

            // Application roles come from the local database, never from the token, so no
            // role claim type is mapped here on purpose.
            RoleClaimType = "__unused_role_claim__",
        };

        if (settings.Enabled)
        {
            jwtOptions.TokenValidationParameters.ValidIssuers = BuildValidIssuers(settings);
        }

    }

    /// <summary>
    /// Accepts the tenant's v2.0 issuer in the forms External ID emits, all pinned to the
    /// configured tenant so a token from another tenant is rejected.
    /// </summary>
    private static List<string> BuildValidIssuers(EntraExternalIdOptions settings)
    {
        List<string> issuers = [];

        if (!string.IsNullOrWhiteSpace(settings.Authority))
        {
            string authority = settings.Authority.TrimEnd('/');
            issuers.Add(authority);
            issuers.Add(authority + "/");

            if (!authority.EndsWith("/v2.0", StringComparison.OrdinalIgnoreCase))
            {
                issuers.Add(authority + "/v2.0");
            }
        }

        return issuers;
    }
}
