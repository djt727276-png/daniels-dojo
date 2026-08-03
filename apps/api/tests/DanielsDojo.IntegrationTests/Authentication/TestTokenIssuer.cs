using System.Security.Cryptography;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DanielsDojo.IntegrationTests.Authentication;

/// <summary>
/// Issues locally signed access tokens so the authentication tests are deterministic and never
/// contact Entra or require an internet tenant.
/// </summary>
/// <remarks>
/// The signing key is an ephemeral RSA key generated per test run and never persisted. The API
/// under test is configured to trust this key, so tokens flow through the real JWT bearer
/// validation pipeline — signature, issuer, audience, and lifetime are all genuinely checked.
/// Nothing here bypasses authentication.
/// </remarks>
public sealed class TestTokenIssuer : IDisposable
{
    /// <summary>Issuer the test host trusts.</summary>
    public const string Issuer = "https://danielsdojo.test/00000000-0000-0000-0000-00000000aaaa/v2.0";

    /// <summary>Tenant identifier placed in the <c>tid</c> claim.</summary>
    public const string TenantId = "00000000-0000-0000-0000-00000000aaaa";

    /// <summary>API client ID and expected audience.</summary>
    public const string ApiClientId = "00000000-0000-0000-0000-00000000bbbb";

    /// <summary>Allowlisted SPA client ID placed in the <c>azp</c> claim.</summary>
    public const string SpaClientId = "00000000-0000-0000-0000-00000000cccc";

    /// <summary>Delegated scope the API requires.</summary>
    public const string RequiredScope = "access_as_user";

    /// <summary>Configured email claim name.</summary>
    public const string EmailClaim = "email";

    private readonly RSA _rsa = RSA.Create(2048);

    /// <summary>Key identifier published in the token header.</summary>
    public string KeyId { get; } = "danielsdojo-test-key";

    /// <summary>The public signing key the API validates against.</summary>
    public SecurityKey PublicSigningKey => new RsaSecurityKey(_rsa.ExportParameters(false))
    {
        KeyId = KeyId,
    };

    /// <summary>
    /// Creates a token. Every parameter has a valid default so a test overrides only the single
    /// value it is asserting on.
    /// </summary>
    public string CreateToken(
        string? objectId = null,
        string? tenantId = TenantId,
        string? issuer = Issuer,
        string? audience = ApiClientId,
        string? scope = RequiredScope,
        string? authorizedParty = SpaClientId,
        string? email = "customer@example.test",
        string? displayName = "Test Customer",
        bool emailVerified = true,
        DateTime? notBefore = null,
        DateTime? expires = null,
        RSA? signingKeyOverride = null)
    {
        List<Claim> claims = [];

        if (!string.IsNullOrEmpty(objectId ?? DefaultObjectId))
        {
            claims.Add(new Claim("oid", objectId ?? DefaultObjectId));
        }

        AddIfPresent(claims, "tid", tenantId);
        AddIfPresent(claims, "scp", scope);
        AddIfPresent(claims, "azp", authorizedParty);
        AddIfPresent(claims, EmailClaim, email);
        AddIfPresent(claims, "name", displayName);
        claims.Add(new Claim("email_verified", emailVerified ? "true" : "false"));

        RsaSecurityKey signingKey = new(signingKeyOverride ?? _rsa) { KeyId = KeyId };

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>Object ID used when a test does not care which customer it is.</summary>
    public const string DefaultObjectId = "11111111-1111-4111-8111-111111111111";

    /// <summary>Creates a token for a specific external object ID.</summary>
    public string CreateTokenForUser(string objectId, string? email = null, string? displayName = null)
        => CreateToken(
            objectId: objectId,
            email: email ?? $"{objectId}@example.test",
            displayName: displayName ?? "Test Customer");

    /// <inheritdoc />
    public void Dispose() => _rsa.Dispose();

    private static void AddIfPresent(List<Claim> claims, string type, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            claims.Add(new Claim(type, value));
        }
    }
}
