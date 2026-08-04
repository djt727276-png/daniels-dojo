namespace DanielsDojo.Api.Authentication;

/// <summary>
/// Settings for the Development-only sign-in harness.
/// </summary>
/// <remarks>
/// This is not a credential store. The local database is never a password provider: the
/// harness issues a short-lived, locally signed token for one of two fixed seeded profiles
/// and accepts nothing else — no arbitrary user ID, email, role, or claim.
/// <para>
/// It is registered only when the host environment is exactly Development **and**
/// <see cref="Enabled"/> is set. Outside Development the endpoint is not mapped at all, so it
/// answers 404 rather than 403.
/// </para>
/// </remarks>
public sealed class DevelopmentAuthOptions
{
    /// <summary>Configuration section binding these settings.</summary>
    public const string SectionName = "Authentication:Development";

    /// <summary>Token issuer, deliberately distinct from the Entra issuer.</summary>
    public const string Issuer = "https://localhost/danielsdojo/development-auth";

    /// <summary>Token audience, deliberately distinct from the Entra API audience.</summary>
    public const string Audience = "danielsdojo-development-api";

    /// <summary>Authentication scheme name, deliberately distinct from the Entra scheme.</summary>
    public const string SchemeName = "DanielsDojoDevelopment";

    /// <summary>Tenant value stamped into the token's <c>tid</c> claim.</summary>
    public const string TenantId = "00000000-0000-4000-8000-0000000d0d00";

    /// <summary>Client value stamped into the token's <c>azp</c> claim.</summary>
    public const string ClientId = "00000000-0000-4000-8000-0000000d0d01";

    /// <summary>
    /// Whether the harness is active. Ignored outside the Development environment.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>How long an issued token remains valid.</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>
    /// Whether the host environment is exactly <c>Development</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately stricter than <c>IHostEnvironment.IsDevelopment()</c>, which matches
    /// case-insensitively. The harness is gated on an exact ordinal match — the same rule the
    /// Phase 2 Development seed guard uses — so a near-miss environment name such as
    /// <c>"development"</c> can never quietly switch on a local credential source.
    /// </remarks>
    public static bool IsExactlyDevelopment(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return string.Equals(
            environment.EnvironmentName,
            Environments.Development,
            StringComparison.Ordinal);
    }
}
