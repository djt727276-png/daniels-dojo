namespace DanielsDojo.Application.Identity;

/// <summary>
/// The minimum local identity a request needs: who the caller is inside Daniel's Dojo and what
/// the local database says they may do. Deliberately carries no token, claim collection, or
/// external identifier — nothing downstream should be able to re-derive authorization from the
/// token instead of from here.
/// </summary>
/// <param name="UserId">Internal Daniel's Dojo user identifier.</param>
/// <param name="DisplayName">Display name for UI.</param>
/// <param name="Email">Contact email.</param>
/// <param name="RoleNames">Application role names held by this user.</param>
public sealed record ApplicationUser(
    Guid UserId,
    string DisplayName,
    string Email,
    IReadOnlyList<string> RoleNames)
{
    /// <summary>Whether the user holds the named application role.</summary>
    public bool IsInRole(string roleName) =>
        RoleNames.Contains(roleName, StringComparer.Ordinal);
}
