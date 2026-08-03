namespace DanielsDojo.Domain.Identity;

/// <summary>Lifecycle state of a platform user account.</summary>
public enum UserStatus
{
    /// <summary>The account may sign in and hold entitlements.</summary>
    Active,

    /// <summary>The account is blocked from signing in. History is retained.</summary>
    Disabled,
}
