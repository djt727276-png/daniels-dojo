namespace DanielsDojo.Domain.Identity;

/// <summary>
/// Assignment of a <see cref="Role"/> to a <see cref="User"/>. Keyed by the pair so a
/// role cannot be assigned twice. All user references are restrictive: assignment
/// history is never removed by deleting a user.
/// </summary>
public sealed class UserRole
{
    /// <summary>User receiving the role.</summary>
    public Guid UserId { get; set; }

    /// <summary>Role being granted.</summary>
    public Guid RoleId { get; set; }

    /// <summary>When the assignment was made, stored UTC.</summary>
    public DateTimeOffset AssignedAtUtc { get; set; }

    /// <summary>Administrator who made the assignment, when known.</summary>
    public Guid? AssignedByUserId { get; set; }

    /// <summary>Optional short justification for the assignment.</summary>
    public string? Reason { get; set; }

    /// <summary>The user receiving the role.</summary>
    public User? User { get; set; }

    /// <summary>The role being granted.</summary>
    public Role? Role { get; set; }

    /// <summary>The administrator who made the assignment.</summary>
    public User? AssignedByUser { get; set; }
}
