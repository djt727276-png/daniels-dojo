namespace DanielsDojo.Application.Community;

/// <summary>Why the community is closed to a member, when it is.</summary>
public enum CommunityAccessDenial
{
    /// <summary>Access is granted.</summary>
    None = 0,

    /// <summary>The member has not completed community profile setup.</summary>
    SetupRequired,

    /// <summary>A moderator suspended the profile.</summary>
    Suspended,

    /// <summary>The member deactivated their own profile.</summary>
    Deactivated,

    /// <summary>The platform account is disabled.</summary>
    AccountDisabled,
}

/// <summary>
/// One decision about whether a member may take part in the community.
/// </summary>
/// <param name="Granted">Whether participation is allowed.</param>
/// <param name="Denial">Why not, when it is not.</param>
/// <param name="ProfileExists">Whether a community profile row exists at all.</param>
/// <param name="Handle">The member's public handle, when they have one.</param>
public sealed record CommunityAccess(
    bool Granted,
    CommunityAccessDenial Denial,
    bool ProfileExists,
    string? Handle)
{
    /// <summary>A sentence safe to return to the member.</summary>
    public string? Message => Denial switch
    {
        CommunityAccessDenial.SetupRequired =>
            "Set up your community profile before taking part.",
        CommunityAccessDenial.Suspended =>
            "Your community profile is suspended. Reading is still available.",
        CommunityAccessDenial.Deactivated =>
            "Your community profile is deactivated. Reactivate it to take part again.",
        CommunityAccessDenial.AccountDisabled =>
            "This account cannot take part in the community.",
        _ => null,
    };
}

/// <summary>
/// The single place that decides whether a member may take part in the community.
/// </summary>
/// <remarks>
/// Every community write consults this, so a later phase can add a requirement — a qualifying
/// entitlement, for example — by changing this one implementation instead of revisiting every
/// endpoint and hoping none was missed.
/// </remarks>
public interface ICommunityAccessEvaluator
{
    /// <summary>Decides whether the member may participate.</summary>
    Task<CommunityAccess> EvaluateAsync(Guid userId, CancellationToken cancellationToken = default);
}
