namespace DanielsDojo.Domain.Identity;

/// <summary>
/// A platform user. Identity is owned by an external provider: the pair
/// (<see cref="ExternalIssuer"/>, <see cref="ExternalSubjectId"/>) is the ownership key,
/// never the email address. No password, reset token, or identity token is stored.
/// </summary>
public sealed class User
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Short name of the identity provider (for example "EntraExternalId").</summary>
    public string IdentityProvider { get; set; } = string.Empty;

    /// <summary>Token issuer that owns <see cref="ExternalSubjectId"/>.</summary>
    public string ExternalIssuer { get; set; } = string.Empty;

    /// <summary>Immutable subject identifier assigned by <see cref="ExternalIssuer"/>.</summary>
    public string ExternalSubjectId { get; set; } = string.Empty;

    /// <summary>Contact email as supplied by the provider.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Upper-cased email used for lookup only. Indexed but deliberately not unique:
    /// account ownership belongs to the issuer/subject pair.
    /// </summary>
    public string NormalizedEmail { get; set; } = string.Empty;

    /// <summary>Name shown in the UI.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Whether the provider asserted the email address is verified.</summary>
    public bool EmailVerified { get; set; }

    /// <summary>Account lifecycle state.</summary>
    public UserStatus Status { get; set; } = UserStatus.Active;

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Roles assigned to this user.</summary>
    public ICollection<UserRole> UserRoles { get; } = new List<UserRole>();
}
