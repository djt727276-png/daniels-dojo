namespace DanielsDojo.Application.Community;

/// <summary>A member's own community profile, including settings only they see.</summary>
public sealed record MyCommunityProfile(
    string Handle,
    string? Bio,
    bool IsDiscoverable,
    string FriendRequestPolicy,
    string MessagePolicy,
    string Status,
    string? GuidelinesVersion,
    DateTimeOffset? GuidelinesAcceptedAtUtc,
    bool EligibilityAttested,
    bool ParticipationReady,
    string RowVersion);

/// <summary>
/// Completes community setup.
/// </summary>
/// <remarks>
/// No date of birth is collected. Eligibility is an attestation the member makes, recorded as
/// a timestamp, which is the least information that satisfies the policy.
/// </remarks>
public sealed record CompleteCommunitySetupRequest(
    string Handle,
    string? Bio,
    bool AcceptGuidelines,
    bool AttestEligibility);

/// <summary>Updates privacy settings and bio. Defaults stay closed until deliberately opened.</summary>
public sealed record UpdateCommunityProfileRequest(
    string? Bio,
    bool IsDiscoverable,
    string FriendRequestPolicy,
    string MessagePolicy,
    string RowVersion);

/// <summary>Community readiness as the member's own screens present it.</summary>
public sealed record CommunityStatusResponse(
    bool Granted,
    string? Denial,
    string? Message,
    bool ProfileExists,
    string? Handle,
    string GuidelinesVersion)
{
    /// <summary>Shapes an access decision for transport.</summary>
    public static CommunityStatusResponse From(CommunityAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);

        return new CommunityStatusResponse(
            access.Granted,
            access.Denial == CommunityAccessDenial.None ? null : access.Denial.ToString(),
            access.Message,
            access.ProfileExists,
            access.Handle,
            Domain.Community.CommunityGuidelines.CurrentVersion);
    }
}

/// <summary>A course the member is enrolled in.</summary>
public sealed record MyCourse(
    string Slug,
    string Title,
    string Summary,
    string Level,
    DateTimeOffset EnrolledAtUtc,
    DateTimeOffset? LastAccessedAtUtc);

/// <summary>
/// The signed-in member's landing view.
/// </summary>
/// <remarks>
/// <see cref="EnrolledCourseCount"/> is a real count of enrollment rows. Purchasing and
/// enrollment arrive in a later phase, so this is legitimately zero today rather than being
/// filled with placeholder content.
/// </remarks>
public sealed record DashboardResponse(
    string DisplayName,
    IReadOnlyList<string> Roles,
    int EnrolledCourseCount,
    int PublishedCourseCount,
    int UnreadNotificationCount,
    int PendingFriendRequestCount,
    int UnreadConversationCount,
    CommunityStatusResponse Community,
    bool PurchasingAvailable);
