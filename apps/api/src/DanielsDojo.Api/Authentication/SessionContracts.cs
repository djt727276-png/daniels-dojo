namespace DanielsDojo.Api.Authentication;

/// <summary>
/// Safe view of the signed-in session. Carries the internal user identifier and the local role
/// names only — never the external object ID, tenant, token, or raw claims.
/// </summary>
/// <param name="UserId">Internal Daniel's Dojo user identifier.</param>
/// <param name="DisplayName">Display name for the UI.</param>
/// <param name="Email">Contact email.</param>
/// <param name="Roles">Application role names from the local database.</param>
public sealed record SessionResponse(
    Guid UserId,
    string DisplayName,
    string Email,
    IReadOnlyList<string> Roles);

/// <summary>
/// Minimal success contract for the admin smoke endpoint. It deliberately returns nothing about
/// the caller beyond confirming the check passed.
/// </summary>
/// <param name="Status">Constant success marker.</param>
/// <param name="CheckedAtUtc">When the check ran.</param>
public sealed record AdminSessionResponse(string Status, DateTimeOffset CheckedAtUtc);
