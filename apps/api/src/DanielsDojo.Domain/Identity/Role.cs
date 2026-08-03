namespace DanielsDojo.Domain.Identity;

/// <summary>A named authorization role. Role membership is stored, never inferred.</summary>
public sealed class Role
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Display name of the role.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Upper-cased unique lookup name.</summary>
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>What the role is for.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Whether the role may be assigned to users by administrators.</summary>
    public bool IsAssignable { get; set; } = true;

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Users holding this role.</summary>
    public ICollection<UserRole> UserRoles { get; } = new List<UserRole>();
}
