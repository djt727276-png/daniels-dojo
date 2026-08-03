namespace DanielsDojo.Application.Identity;

/// <summary>
/// Scoped access to the local user behind the current request. Populated once, after token
/// validation and before authorization, so application code never re-reads the token.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The resolved local user, or null on an anonymous request.</summary>
    ApplicationUser? User { get; }
}
