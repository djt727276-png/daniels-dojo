using DanielsDojo.Application.Catalog;

namespace DanielsDojo.Application.Community;

/// <summary>A forum category with a live thread count.</summary>
public sealed record ForumCategorySummary(
    Guid Id,
    string Slug,
    string Name,
    string Description,
    int SortOrder,
    int ThreadCount,
    DateTimeOffset? LastActivityAtUtc);

/// <summary>A thread as it appears in a category listing.</summary>
public sealed record ForumThreadSummary(
    Guid Id,
    string Title,
    string CategorySlug,
    string AuthorHandle,
    bool AuthorHidden,
    string Status,
    bool IsPinned,
    int ReplyCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastActivityAtUtc);

/// <summary>
/// A post as a reader sees it.
/// </summary>
/// <remarks>
/// <see cref="Body"/> is empty whenever the post is a tombstone or the reader has blocked the
/// author. The row still appears so replies keep their position and the conversation still
/// reads as a conversation, but no withheld text is ever sent to the browser.
/// </remarks>
public sealed record ForumPostView(
    Guid Id,
    Guid? ReplyToPostId,
    string AuthorHandle,
    bool AuthorHidden,
    bool IsOwn,
    string Body,
    string Status,
    bool Withheld,
    string? WithheldReason,
    int LikeCount,
    bool LikedByMe,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? EditedAtUtc,
    string RowVersion);

/// <summary>A thread with a page of its posts.</summary>
public sealed record ForumThreadDetail(
    Guid Id,
    string Title,
    string CategorySlug,
    string CategoryName,
    string AuthorHandle,
    string Status,
    bool IsPinned,
    bool AcceptsReplies,
    bool Subscribed,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastActivityAtUtc,
    PagedResult<ForumPostView> Posts,
    string RowVersion);

/// <summary>Starts a thread with its opening post.</summary>
public sealed record CreateThreadRequest(string CategorySlug, string Title, string Body);

/// <summary>Adds a reply to a thread.</summary>
public sealed record CreatePostRequest(string Body, Guid? ReplyToPostId);

/// <summary>Edits an existing post.</summary>
public sealed record UpdatePostRequest(string Body, string RowVersion);

/// <summary>Reports a profile, thread, post, or message.</summary>
public sealed record CreateReportRequest(
    string TargetType,
    Guid TargetId,
    string ReasonCode,
    string? Detail);

/// <summary>A moderation queue entry.</summary>
public sealed record ModerationReport(
    Guid Id,
    string TargetType,
    Guid TargetId,
    string ReasonCode,
    string? Detail,
    string Status,
    string ReporterHandle,
    string? HandledByDisplayName,
    string? Resolution,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? HandledAtUtc,
    string RowVersion);

/// <summary>
/// The reported item, as much of it as moderating it requires and no more.
/// </summary>
/// <remarks>
/// This is the only way a moderator can read a private message, and it returns exactly one
/// message — never the conversation around it, never the other party's other messages, and
/// never anything that was not reported. Reading it is itself audited.
/// </remarks>
/// <param name="ReportId">The report this view was unlocked by.</param>
/// <param name="TargetType">What kind of thing was reported.</param>
/// <param name="TargetId">Identifier of the reported thing.</param>
/// <param name="AuthorHandle">Community handle of whoever wrote it.</param>
/// <param name="Status">Current status of the reported record.</param>
/// <param name="Content">The reported text, or empty when it has already been tombstoned.</param>
/// <param name="Context">One short line naming where it sits, without quoting anything else.</param>
/// <param name="CreatedAtUtc">When the reported item was written.</param>
public sealed record ModerationTarget(
    Guid ReportId,
    string TargetType,
    Guid TargetId,
    string AuthorHandle,
    string Status,
    string Content,
    string? Context,
    DateTimeOffset CreatedAtUtc);

/// <summary>A moderator decision, always carrying a reason.</summary>
public sealed record ModerationDecisionRequest(string Reason, string RowVersion);

/// <summary>A moderator action on content, which has no client-held row version.</summary>
public sealed record ModerationActionRequest(string Reason);
