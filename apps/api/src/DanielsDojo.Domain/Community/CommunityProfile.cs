using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Community;

/// <summary>
/// A member's public community identity.
/// </summary>
/// <remarks>
/// Keyed by <see cref="UserId"/>, so a member has at most one profile and the platform
/// account remains the single identity. Discovery, friend requests, and messaging all
/// default to their most private values until the member completes setup, so a freshly
/// provisioned account is invisible rather than exposed.
/// <para>
/// No birth date is stored. Eligibility is recorded only as an attestation timestamp
/// alongside the accepted guidelines version.
/// </para>
/// </remarks>
public sealed class CommunityProfile
{
    /// <summary>Owning platform user. Primary key and foreign key.</summary>
    public Guid UserId { get; set; }

    /// <summary>Public handle as the member typed it.</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>Upper-cased handle used for uniqueness and lookup.</summary>
    public string NormalizedHandle { get; set; } = string.Empty;

    /// <summary>Short self-description.</summary>
    public string? Bio { get; set; }

    /// <summary>Blob object name for a future avatar. No binary upload exists yet.</summary>
    public string? AvatarStorageKey { get; set; }

    /// <summary>Whether the profile appears in handle search. Opt-in.</summary>
    public bool IsDiscoverable { get; set; }

    /// <summary>Who may send a friend request.</summary>
    public FriendRequestPolicy FriendRequestPolicy { get; set; } = FriendRequestPolicy.NoOne;

    /// <summary>Who may send a direct message.</summary>
    public MessagePolicy MessagePolicy { get; set; } = MessagePolicy.NoOne;

    /// <summary>Profile lifecycle state.</summary>
    public CommunityProfileStatus Status { get; set; } = CommunityProfileStatus.Active;

    /// <summary>Version of the community guidelines the member accepted.</summary>
    public string? GuidelinesVersion { get; set; }

    /// <summary>When the guidelines were accepted, stored UTC.</summary>
    public DateTimeOffset? GuidelinesAcceptedAtUtc { get; set; }

    /// <summary>
    /// When the member attested they meet the age policy, stored UTC. The date of birth
    /// itself is deliberately never collected.
    /// </summary>
    public DateTimeOffset? EligibilityAttestedAtUtc { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The owning platform user.</summary>
    public User? User { get; set; }

    /// <summary>
    /// Whether the member has completed everything required before creating content,
    /// sending friend requests, or messaging.
    /// </summary>
    public bool IsParticipationReady =>
        Status == CommunityProfileStatus.Active
        && !string.IsNullOrWhiteSpace(NormalizedHandle)
        && GuidelinesAcceptedAtUtc is not null
        && EligibilityAttestedAtUtc is not null;
}
