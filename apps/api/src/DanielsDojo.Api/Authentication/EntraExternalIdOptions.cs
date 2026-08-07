using System.ComponentModel.DataAnnotations;

namespace DanielsDojo.Api.Authentication;

/// <summary>
/// Environment-specific Entra External ID settings. These are public identifiers only — there
/// is deliberately no client secret, certificate, or credential of any kind here, because the
/// API never calls the identity provider on its own behalf in Phase 3.
/// </summary>
public sealed class EntraExternalIdOptions
{
    /// <summary>Configuration section binding these settings.</summary>
    public const string SectionName = "Authentication:EntraExternalId";

    /// <summary>
    /// Whether real bearer authentication is enabled. When false the API still registers the
    /// authentication scheme but performs no metadata discovery, which is what lets the
    /// integration tests substitute a local issuer and signing key.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Authority for the external tenant, for example
    /// <c>https://contoso.ciamlogin.com/00000000-0000-0000-0000-000000000000/v2.0</c>.
    /// </summary>
    [Url]
    public string Authority { get; set; } = string.Empty;

    /// <summary>External tenant identifier. Matched against the token's <c>tid</c> claim.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// This API's application (client) ID. Also the expected token audience, accepted both bare
    /// and in <c>api://</c> form.
    /// </summary>
    public string ApiClientId { get; set; } = string.Empty;

    /// <summary>Delegated scope every protected request must carry.</summary>
    public string RequiredScope { get; set; } = "access_as_user";

    /// <summary>
    /// Client IDs permitted to call this API, matched against the token's <c>azp</c> claim. Only
    /// the Daniel's Dojo SPA belongs here: a token minted for a different client must not be
    /// accepted even when it carries a valid user identity.
    /// </summary>
    public IList<string> AllowedClientIds { get; } = [];

    /// <summary>
    /// Claim carrying the customer's email, as actually observed on the user flow. External ID
    /// varies this between <c>email</c>, <c>emails</c>, and <c>preferred_username</c>, so it is
    /// configured rather than guessed.
    /// </summary>
    public string EmailClaimName { get; set; } = "email";

    /// <summary>
    /// Whether the tenant's own sign-up flow verifies the email address. External ID's
    /// email one-time-passcode flows prove ownership of the address at sign-up yet emit no
    /// <c>email_verified</c> claim in their tokens, so a deployment on such a tenant states
    /// the fact here explicitly. When true, a token carrying the configured email claim but
    /// no verification claim at all is treated as verified; a token that explicitly asserts
    /// <c>email_verified: false</c> is still unverified. Off by default.
    /// </summary>
    public bool EmailVerifiedByUserFlow { get; set; }

    /// <summary>Exact browser origin allowed to call the API during development.</summary>
    [Url]
    public string AllowedCorsOrigin { get; set; } = "http://localhost:4200";

    /// <summary>
    /// Audience values accepted for this API. Entra issues <c>aud</c> as either the bare client
    /// ID or the <c>api://</c> resource URI depending on the token version.
    /// </summary>
    public IReadOnlyList<string> ValidAudiences =>
        string.IsNullOrWhiteSpace(ApiClientId)
            ? []
            : [ApiClientId, $"api://{ApiClientId}"];
}
