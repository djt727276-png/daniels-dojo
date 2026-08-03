namespace DanielsDojo.Application.Identity;

/// <summary>
/// Resolves a validated external identity to a local Daniel's Dojo user, creating the record on
/// first sign-in. This is a narrow, purpose-built contract for the authentication slice — not a
/// generic repository.
/// </summary>
public interface IUserProvisioningService
{
    /// <summary>
    /// Finds the local user owning <paramref name="identity"/>, or creates it exactly once.
    /// Implementations must be safe against concurrent first requests for the same identity and
    /// must never remove or downgrade roles a user already holds.
    /// </summary>
    Task<UserProvisioningResult> ResolveAsync(
        ExternalUserIdentity identity,
        CancellationToken cancellationToken = default);
}
