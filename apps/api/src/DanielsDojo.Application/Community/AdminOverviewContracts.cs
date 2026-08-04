namespace DanielsDojo.Application.Community;

/// <summary>One recent privileged action, shaped for the operator dashboard.</summary>
/// <remarks>
/// Carries the same safe fields the audit table stores: who, what, which record, and the
/// reason where one was required. No metadata blob is forwarded, so nothing a moderator typed
/// about a member and nothing a member wrote can arrive here by accident.
/// </remarks>
public sealed record AuditActivityEntry(
    Guid Id,
    string Action,
    string TargetType,
    string TargetId,
    string ActorDisplayName,
    string? Reason,
    DateTimeOffset OccurredAtUtc);

/// <summary>Everything the Admin landing page needs, in one round trip.</summary>
public sealed record AdminOverview(
    int DraftCourses,
    int PublishedCourses,
    int ArchivedCourses,
    int CoursesReadyToPublish,
    int ActiveOffers,
    int DraftOffers,
    int OpenReports,
    int ReviewingReports,
    int ForumCategories,
    IReadOnlyList<AuditActivityEntry> RecentActivity);

/// <summary>A forum category as the operator manages it, including archived ones.</summary>
public sealed record AdminForumCategory(
    Guid Id,
    string Slug,
    string Name,
    string Description,
    int SortOrder,
    string Status,
    int ThreadCount,
    string RowVersion);

/// <summary>Creates a forum category.</summary>
public sealed record CreateForumCategoryRequest(
    string Slug,
    string Name,
    string Description,
    int SortOrder);

/// <summary>Updates a forum category's presentation. The slug is fixed once created.</summary>
public sealed record UpdateForumCategoryRequest(
    string Name,
    string Description,
    int SortOrder,
    string RowVersion);
