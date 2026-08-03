namespace DanielsDojo.Application.Identity;

/// <summary>Why a sign-in was refused after the token itself validated successfully.</summary>
public enum UserProvisioningFailure
{
    /// <summary>No failure.</summary>
    None = 0,

    /// <summary>The token lacked the immutable identity claims required to own a local record.</summary>
    MissingIdentityClaims,

    /// <summary>A new customer arrived without the configured email claim.</summary>
    MissingEmailClaim,

    /// <summary>The local account exists but has been disabled by an administrator.</summary>
    UserDisabled,
}

/// <summary>
/// Outcome of resolving an external identity to a local user. Failures are deliberately coarse:
/// the caller turns them into a plain 403 without echoing which check tripped.
/// </summary>
public sealed record UserProvisioningResult
{
    private UserProvisioningResult()
    {
    }

    /// <summary>The resolved local user, when successful.</summary>
    public ApplicationUser? User { get; private init; }

    /// <summary>Why resolution failed, when unsuccessful.</summary>
    public UserProvisioningFailure Failure { get; private init; }

    /// <summary>Whether a new local user record was created by this request.</summary>
    public bool WasProvisioned { get; private init; }

    /// <summary>Whether resolution succeeded.</summary>
    public bool Succeeded => User is not null;

    /// <summary>Creates a successful result.</summary>
    public static UserProvisioningResult Success(ApplicationUser user, bool wasProvisioned) =>
        new() { User = user, WasProvisioned = wasProvisioned };

    /// <summary>Creates a failed result.</summary>
    public static UserProvisioningResult Denied(UserProvisioningFailure failure) =>
        new() { Failure = failure };
}
