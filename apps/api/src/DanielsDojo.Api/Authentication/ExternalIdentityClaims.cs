using System.Security.Claims;
using DanielsDojo.Application.Identity;

namespace DanielsDojo.Api.Authentication;

/// <summary>
/// Reads the validated token's claims. Everything here runs only after the JWT bearer handler
/// has verified the signature, issuer, audience, and lifetime.
/// </summary>
internal static class ExternalIdentityClaims
{
    /// <summary>Tenant identifier claim.</summary>
    public const string TenantId = "tid";

    /// <summary>Immutable object identifier claim.</summary>
    public const string ObjectId = "oid";

    /// <summary>Authorized party (calling client) claim.</summary>
    public const string AuthorizedParty = "azp";

    /// <summary>Space-delimited delegated scopes.</summary>
    public const string Scope = "scp";

    private const string ObjectIdLongForm =
        "http://schemas.microsoft.com/identity/claims/objectidentifier";

    private const string TenantIdLongForm =
        "http://schemas.microsoft.com/identity/claims/tenantid";

    /// <summary>
    /// Builds the external identity from the principal, or null when either immutable claim is
    /// absent. A token without <c>oid</c> cannot own a local record, no matter how valid its
    /// signature is.
    /// </summary>
    public static ExternalUserIdentity? TryRead(ClaimsPrincipal principal, EntraExternalIdOptions options)
    {
        string? tenantId = First(principal, TenantId, TenantIdLongForm);
        string? objectId = First(principal, ObjectId, ObjectIdLongForm);

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(objectId))
        {
            return null;
        }

        return new ExternalUserIdentity(
            TenantId: tenantId,
            ObjectId: objectId,
            Email: ReadEmail(principal, options),
            DisplayName: First(principal, "name", ClaimTypes.Name),
            EmailVerified: ReadEmailVerified(principal));
    }

    /// <summary>Whether the token carries the required delegated scope.</summary>
    public static bool HasRequiredScope(ClaimsPrincipal principal, string requiredScope)
    {
        string? scopes = First(principal, Scope, "http://schemas.microsoft.com/identity/claims/scope");

        return scopes is not null
            && scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(requiredScope, StringComparer.Ordinal);
    }

    /// <summary>Whether the calling client is on the allowlist.</summary>
    public static bool IsAllowedClient(ClaimsPrincipal principal, IEnumerable<string> allowedClientIds)
    {
        string? authorizedParty = First(principal, AuthorizedParty, "appid");

        return authorizedParty is not null
            && allowedClientIds.Contains(authorizedParty, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ReadEmail(ClaimsPrincipal principal, EntraExternalIdOptions options)
    {
        // Only the configured claim is trusted. External ID varies which claim carries the
        // address between user flows, so guessing across several would risk picking up an
        // attacker-influenced value.
        string? email = principal.FindFirst(options.EmailClaimName)?.Value;

        return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    private static bool ReadEmailVerified(ClaimsPrincipal principal)
    {
        string? value = First(principal, "email_verified", "verified_primary_email");

        return bool.TryParse(value, out bool verified) && verified;
    }

    private static string? First(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (string claimType in claimTypes)
        {
            string? value = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
