using DanielsDojo.Application.Catalog;
using DanielsDojo.Application.Common;

namespace DanielsDojo.Application.Community;

/// <summary>
/// Moderator actions over community content and profiles.
/// </summary>
/// <remarks>
/// Every action requires a reason and writes one audit record in the same transaction as the
/// change, so no moderation decision is anonymous and none can be lost. Content is tombstoned
/// rather than deleted: the row survives with its body cleared, which keeps replies in place
/// and keeps the decision reviewable.
/// </remarks>
public interface IModerationService
{
    /// <summary>Builds the Admin landing summary, including recent privileged activity.</summary>
    Task<AdminOverview> GetOverviewAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists every forum category, including archived ones.</summary>
    Task<IReadOnlyList<AdminForumCategory>> ListCategoriesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Creates a forum category.</summary>
    Task<OperationResult<AdminForumCategory>> CreateCategoryAsync(
        CreateForumCategoryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a category name, description, or position.</summary>
    Task<OperationResult<AdminForumCategory>> UpdateCategoryAsync(
        Guid categoryId,
        UpdateForumCategoryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Archives or reactivates a category. Threads are always retained.</summary>
    Task<OperationResult<AdminForumCategory>> SetCategoryStatusAsync(
        Guid categoryId,
        string targetStatus,
        ModerationDecisionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Lists reports, newest first, optionally filtered by status.</summary>
    Task<PagedResult<ModerationReport>> ListReportsAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the reported item so a moderator can judge it.
    /// </summary>
    /// <remarks>
    /// This is deliberately the only route to a private message's text. It is keyed on an open
    /// report, returns that one target and nothing around it, and records the read in the audit
    /// trail — so moderation cannot quietly become general surveillance.
    /// </remarks>
    Task<OperationResult<ModerationTarget>> GetReportTargetAsync(
        Guid moderatorUserId,
        Guid reportId,
        CancellationToken cancellationToken = default);

    /// <summary>Moves a report through its lifecycle.</summary>
    Task<OperationResult<ModerationReport>> DecideReportAsync(
        Guid moderatorUserId,
        Guid reportId,
        string targetStatus,
        ModerationDecisionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Tombstones a post.</summary>
    Task<OperationResult> RemovePostAsync(
        Guid moderatorUserId,
        Guid postId,
        ModerationActionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Locks, archives, reopens, or removes a thread.</summary>
    Task<OperationResult> SetThreadStatusAsync(
        Guid moderatorUserId,
        Guid threadId,
        string targetStatus,
        ModerationActionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Pins or unpins a thread.</summary>
    Task<OperationResult> SetThreadPinnedAsync(
        Guid moderatorUserId,
        Guid threadId,
        bool pinned,
        ModerationActionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Suspends or reinstates a community profile.</summary>
    Task<OperationResult> SetProfileStatusAsync(
        Guid moderatorUserId,
        Guid targetUserId,
        string targetStatus,
        ModerationActionRequest request,
        CancellationToken cancellationToken = default);
}
