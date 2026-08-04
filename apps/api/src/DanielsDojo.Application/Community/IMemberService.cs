using DanielsDojo.Application.Common;

namespace DanielsDojo.Application.Community;

/// <summary>The signed-in member's own dashboard, learning list, and community profile.</summary>
public interface IMemberService
{
    /// <summary>Builds the member's landing view.</summary>
    Task<DashboardResponse> GetDashboardAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the courses the member is enrolled in.</summary>
    Task<IReadOnlyList<MyCourse>> GetMyCoursesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the member's own community profile, or null before setup.</summary>
    Task<MyCommunityProfile?> GetCommunityProfileAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates the community profile, recording guidelines acceptance and eligibility.</summary>
    Task<OperationResult<MyCommunityProfile>> CompleteCommunitySetupAsync(
        Guid userId,
        CompleteCommunitySetupRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates bio and privacy settings.</summary>
    Task<OperationResult<MyCommunityProfile>> UpdateCommunityProfileAsync(
        Guid userId,
        UpdateCommunityProfileRequest request,
        CancellationToken cancellationToken = default);
}
