namespace DanielsDojo.Application.Identity;

/// <summary>
/// The one-time launch-administrator bootstrap.
/// </summary>
/// <remarks>
/// <para>
/// Daniel's Dojo launches with exactly one designated application administrator, named by
/// email in protected configuration — never in source. The email is only the *invitation*:
/// it decides which first sign-in receives the role. The role itself binds to the immutable
/// Entra (issuer, subject) pair through the local user row, so a later change of email
/// neither transfers nor revokes anything, and no request is ever authorized by comparing
/// email addresses.
/// </para>
/// <para>
/// The bootstrap is consumed the moment any Admin assignment exists. After that this
/// configuration is inert: a second account presenting the same address gets Student like
/// everyone else. Recovery, if the Admin identity is ever lost, is the operator-only
/// <c>identity grant-admin</c> command, which requires database credentials and has no HTTP
/// surface.
/// </para>
/// </remarks>
public sealed class AdminBootstrapOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Authentication";

    /// <summary>
    /// Email of the designated launch administrator. Empty disables the bootstrap entirely.
    /// </summary>
    public string BootstrapAdminEmail { get; set; } = string.Empty;
}
