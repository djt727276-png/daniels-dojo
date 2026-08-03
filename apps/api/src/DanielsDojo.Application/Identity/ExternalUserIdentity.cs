namespace DanielsDojo.Application.Identity;

/// <summary>
/// The validated external identity extracted from an access token.
/// </summary>
/// <param name="TenantId">
/// The token's <c>tid</c>. Immutable, and half of the local ownership key.
/// </param>
/// <param name="ObjectId">
/// The token's <c>oid</c>. Immutable for the life of the account and stable across every
/// application in the tenant, unlike the pairwise <c>sub</c>.
/// </param>
/// <param name="Email">
/// Email from the configured claim, or null when the token carries none.
/// </param>
/// <param name="DisplayName">Display name from the token, or null.</param>
/// <param name="EmailVerified">Whether the provider asserted the address is verified.</param>
public sealed record ExternalUserIdentity(
    string TenantId,
    string ObjectId,
    string? Email,
    string? DisplayName,
    bool EmailVerified);
