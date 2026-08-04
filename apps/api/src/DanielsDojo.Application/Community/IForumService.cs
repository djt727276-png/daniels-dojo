using DanielsDojo.Application.Catalog;
using DanielsDojo.Application.Common;

namespace DanielsDojo.Application.Community;

/// <summary>
/// Reading and writing forum content as a signed-in member.
/// </summary>
/// <remarks>
/// Every write consults <see cref="ICommunityAccessEvaluator"/> first, so participation rules
/// live in one place. Every read is told who is asking, because blocks and ownership change
/// what a given member is allowed to see.
/// </remarks>
public interface IForumService
{
    /// <summary>Lists active categories.</summary>
    Task<IReadOnlyList<ForumCategorySummary>> ListCategoriesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The newest threads across every active category, for the community landing page.
    /// </summary>
    Task<IReadOnlyList<ForumThreadSummary>> ListRecentThreadsAsync(
        Guid readerUserId,
        int count,
        CancellationToken cancellationToken = default);

    /// <summary>Lists threads in a category, pinned first.</summary>
    Task<OperationResult<PagedResult<ForumThreadSummary>>> ListThreadsAsync(
        Guid readerUserId,
        string categorySlug,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a thread with a page of posts.</summary>
    Task<OperationResult<ForumThreadDetail>> GetThreadAsync(
        Guid readerUserId,
        Guid threadId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Starts a thread and its opening post.</summary>
    Task<OperationResult<ForumThreadDetail>> CreateThreadAsync(
        Guid authorUserId,
        CreateThreadRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a reply and notifies subscribers.</summary>
    Task<OperationResult<ForumThreadDetail>> CreatePostAsync(
        Guid authorUserId,
        Guid threadId,
        CreatePostRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Edits the caller's own post.</summary>
    Task<OperationResult<ForumThreadDetail>> UpdatePostAsync(
        Guid authorUserId,
        Guid postId,
        UpdatePostRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Tombstones the caller's own post.</summary>
    Task<OperationResult<ForumThreadDetail>> RemoveOwnPostAsync(
        Guid authorUserId,
        Guid postId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds or removes the caller's like on a post.</summary>
    Task<OperationResult<ForumThreadDetail>> SetReactionAsync(
        Guid userId,
        Guid postId,
        bool liked,
        CancellationToken cancellationToken = default);

    /// <summary>Subscribes or unsubscribes the caller from a thread.</summary>
    Task<OperationResult<ForumThreadDetail>> SetSubscriptionAsync(
        Guid userId,
        Guid threadId,
        bool subscribed,
        CancellationToken cancellationToken = default);

    /// <summary>Files a report for moderator review.</summary>
    Task<OperationResult> ReportAsync(
        Guid reporterUserId,
        CreateReportRequest request,
        CancellationToken cancellationToken = default);
}
