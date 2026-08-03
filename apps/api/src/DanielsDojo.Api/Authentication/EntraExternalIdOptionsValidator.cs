using Microsoft.Extensions.Options;

namespace DanielsDojo.Api.Authentication;

/// <summary>
/// Fails fast with actionable messages when the authentication configuration is missing,
/// malformed, or absent in an environment that requires it.
/// </summary>
/// <remarks>
/// Two independent rules, neither conditional on Development:
/// <list type="bullet">
/// <item>Whenever <see cref="EntraExternalIdOptions.Enabled"/> is set, every value must be
/// present and well formed.</item>
/// <item>In Production, authentication must be enabled at all. Disabled mode is a local
/// convenience; a Production host must never start having quietly skipped it.</item>
/// </list>
/// </remarks>
internal sealed class EntraExternalIdOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<EntraExternalIdOptions>
{
    public ValidateOptionsResult Validate(string? name, EntraExternalIdOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            // Disabled mode never makes anything public — protected endpoints still reject every
            // caller because no signing key, issuer, or audience is configured. It is refused in
            // Production regardless, so a deployment cannot silently run without authentication.
            if (environment.IsProduction())
            {
                return ValidateOptionsResult.Fail(
                    $"{Key(nameof(options.Enabled))} must be true in the Production environment. " +
                    "Authentication-disabled mode is a local development convenience only. " +
                    "Supply the Entra External ID settings through environment variables or your " +
                    "deployment's configuration provider, or host this instance under a " +
                    "non-Production environment name.");
            }

            // Nothing else to validate, and no placeholder value is treated as if it were real.
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [];

        RequireAbsoluteUri(options.Authority, nameof(options.Authority), failures);
        RequireGuid(options.TenantId, nameof(options.TenantId), failures);
        RequireGuid(options.ApiClientId, nameof(options.ApiClientId), failures);
        RequireNonEmpty(options.RequiredScope, nameof(options.RequiredScope), failures);
        RequireNonEmpty(options.EmailClaimName, nameof(options.EmailClaimName), failures);
        RequireAbsoluteUri(options.AllowedCorsOrigin, nameof(options.AllowedCorsOrigin), failures);

        if (options.AllowedClientIds.Count == 0)
        {
            failures.Add(
                $"{Key(nameof(options.AllowedClientIds))} must list at least one SPA client ID. " +
                "This is the authorized-party (azp) allowlist; without it any client with a user " +
                "identity could call this API.");
        }
        else
        {
            for (int index = 0; index < options.AllowedClientIds.Count; index++)
            {
                RequireGuid(
                    options.AllowedClientIds[index],
                    $"{nameof(options.AllowedClientIds)}:{index}",
                    failures);
            }
        }

        if (!string.IsNullOrWhiteSpace(options.TenantId)
            && !string.IsNullOrWhiteSpace(options.Authority)
            && !options.Authority.Contains(options.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"{Key(nameof(options.Authority))} does not contain " +
                $"{Key(nameof(options.TenantId))}. The authority must address the same external " +
                "tenant the API validates tokens for.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static string Key(string property) => $"{EntraExternalIdOptions.SectionName}:{property}";

    private static void RequireNonEmpty(string value, string property, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{Key(property)} is required when authentication is enabled.");
        }
    }

    private static void RequireGuid(string value, string property, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(
                $"{Key(property)} is required when authentication is enabled. Supply the value " +
                "from the Entra app registration; it is a public identifier, not a secret.");
            return;
        }

        if (!Guid.TryParse(value, out _))
        {
            // Deliberately does not echo the value: configuration content stays out of logs.
            failures.Add($"{Key(property)} must be a GUID.");
        }
    }

    private static void RequireAbsoluteUri(string value, string property, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{Key(property)} is required when authentication is enabled.");
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"{Key(property)} must be an absolute http or https URI.");
        }
    }
}
