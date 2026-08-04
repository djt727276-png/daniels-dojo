using System.Security.Claims;
using DanielsDojo.Infrastructure.Persistence.Seeding;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DanielsDojo.Api.Authentication;

/// <summary>Request body for the Development sign-in endpoint.</summary>
/// <param name="Profile">One of the two allowlisted seeded profile keys.</param>
public sealed record DevelopmentTokenRequest(string? Profile);

/// <summary>Issued Development token.</summary>
/// <param name="AccessToken">Short-lived, locally signed bearer token.</param>
/// <param name="ExpiresAtUtc">When the token stops being accepted.</param>
public sealed record DevelopmentTokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// The Development-only sign-in endpoint.
/// </summary>
/// <remarks>
/// Mapped only when the host environment is exactly Development and the harness is enabled, so
/// outside Development the route does not exist and the API answers 404 rather than 403 — an
/// absent endpoint leaks nothing about why.
/// <para>
/// The endpoint is an allowlist, not a login form: it accepts one of two fixed profile keys
/// and nothing else. There is no way to request an arbitrary user ID, email, role, or claim,
/// and no password is involved because the local database is not a credential store.
/// </para>
/// </remarks>
internal static class DevelopmentAuthEndpoints
{
    /// <summary>Profile key for the seeded administrator.</summary>
    public const string AdminProfileKey = "admin";

    /// <summary>Profile key for the seeded student.</summary>
    public const string StudentProfileKey = "student";

    /// <summary>Maps the endpoint. The caller decides whether it should be mapped at all.</summary>
    public static void MapDevelopmentAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/development/auth/token", (
                DevelopmentTokenRequest request,
                IOptions<DevelopmentAuthOptions> options,
                DevelopmentSigningKey signingKey,
                TimeProvider timeProvider) =>
            {
                if (!TryResolveProfile(request.Profile, out string subject, out string email, out string name))
                {
                    // Deliberately does not echo the requested value.
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["profile"] =
                            [
                                $"Must be '{AdminProfileKey}' or '{StudentProfileKey}'. " +
                                "The Development harness issues tokens for seeded profiles only.",
                            ],
                        },
                        title: "Unknown development profile");
                }

                DevelopmentAuthOptions settings = options.Value;
                DateTimeOffset now = timeProvider.GetUtcNow();
                DateTimeOffset expires = now.Add(settings.TokenLifetime);

                // The claim set is exactly what the existing local-user resolution needs: the
                // immutable (tid, oid) pair, the configured email claim, a display name, the
                // required scope, and the authorized party. No role claim is issued — roles
                // come from the local database.
                SecurityTokenDescriptor descriptor = new()
                {
                    Issuer = DevelopmentAuthOptions.Issuer,
                    Audience = DevelopmentAuthOptions.Audience,
                    NotBefore = now.UtcDateTime,
                    Expires = expires.UtcDateTime,
                    SigningCredentials = signingKey.CreateSigningCredentials(),
                    Subject = new ClaimsIdentity(
                    [
                        new Claim("tid", DatabaseSeeder.DevelopmentSeedIssuer),
                        new Claim("oid", subject),
                        new Claim("azp", DevelopmentAuthOptions.ClientId),
                        new Claim("scp", "access_as_user"),
                        new Claim("email", email),
                        new Claim("name", name),
                        new Claim("email_verified", "true"),
                    ]),
                };

                string token = new JsonWebTokenHandler().CreateToken(descriptor);

                return Results.Ok(new DevelopmentTokenResponse(token, expires));
            })
            .AllowAnonymous()
            .WithName("DevelopmentSignIn");
    }

    /// <summary>
    /// Resolves a profile key to its seeded identity. Returns false for anything else, which
    /// is what keeps this an allowlist rather than a lookup.
    /// </summary>
    private static bool TryResolveProfile(
        string? profile,
        out string subject,
        out string email,
        out string displayName)
    {
        switch (profile)
        {
            case AdminProfileKey:
                subject = DatabaseSeeder.DevelopmentSeedSubject;
                email = DatabaseSeeder.DevelopmentAdminEmail;
                displayName = "Development Admin";
                return true;

            case StudentProfileKey:
                subject = DatabaseSeeder.DevelopmentSeedStudentSubject;
                email = DatabaseSeeder.DevelopmentStudentEmail;
                displayName = "Development Student";
                return true;

            default:
                subject = string.Empty;
                email = string.Empty;
                displayName = string.Empty;
                return false;
        }
    }
}
