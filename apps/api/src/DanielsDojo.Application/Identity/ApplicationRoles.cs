namespace DanielsDojo.Application.Identity;

/// <summary>
/// Application role names as seeded in Phase 2. These are the authoritative permission names —
/// the local database decides what a user may do, never a claim supplied by the client.
/// </summary>
public static class ApplicationRoles
{
    /// <summary>Every provisioned customer receives exactly this role on first sign-in.</summary>
    public const string Student = "Student";

    /// <summary>Granted only by the explicit, audited operator command.</summary>
    public const string Admin = "Admin";
}
