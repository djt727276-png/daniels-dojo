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

    /// <summary>
    /// Registers configuration, bearer validation, and role policies. Configuration is validated
    /// on start so a misconfigured deployment fails immediately rather than at the first
    /// sign-in attempt.
    /// </summary>
    public static IServiceCollection AddDanielsDojoAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<EntraExternalIdOptions>()
            .Bind(configuration.GetSection(EntraExternalIdOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<EntraExternalIdOptions>>(
            provider => new EntraExternalIdOptionsValidator(
                provider.GetRequiredService<IHostEnvironment>()));

        EntraExternalIdOptions settings = new();
        configuration.GetSection(EntraExternalIdOptions.SectionName).Bind(settings);

        AuthenticationBuilder authentication =
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

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

                // Suppress the default WWW-Authenticate detail so a rejected token never
                // explains which validation step failed.
                jwtOptions.Events.OnChallenge = static challengeContext =>
                {
                    challengeContext.HandleResponse();
                    challengeContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
            });

        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<CurrentUserAccessor>());
        services.AddScoped<IAuthorizationHandler, ApplicationRoleHandler>();

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
