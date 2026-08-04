using DanielsDojo.Application.Catalog;
using DanielsDojo.Application.Common;
using DanielsDojo.Application.Community;
using DanielsDojo.Domain.Community;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DanielsDojo.Infrastructure.Community;

/// <summary>
/// Reading and writing forum content.
/// </summary>
/// <remarks>
/// <para>
/// Two rules shape every method. Participation is decided once, by
/// <see cref="ICommunityAccessEvaluator"/>, so no endpoint can forget a check. And a block is
/// symmetric for reading: if either member has blocked the other, the post row still appears —
/// so replies keep their place — but the body is never sent, rather than being sent and hidden
/// with CSS.
/// </para>
/// <para>
/// Bodies are plain text throughout. Nothing renders them as HTML or Markdown, so a post
/// cannot carry active content into another member's browser.
/// </para>
/// </remarks>
internal sealed class ForumService : IForumService
{
    /// <summary>Largest page of posts or threads a caller may request.</summary>
    private const int MaxPageSize = 50;

    /// <summary>Default page size.</summary>
    private const int DefaultPageSize = 20;

    /// <summary>Longest accepted post body.</summary>
    private const int MaxBodyLength = 8000;

    /// <summary>Longest accepted thread title.</summary>
    private const int MaxTitleLength = 200;

    /// <summary>Hard ceiling on the category list, which has no paging of its own.</summary>
    private const int MaxCategoryListSize = 100;

    private readonly DanielsDojoDbContext context;
    private readonly ICommunityAccessEvaluator accessEvaluator;
    private readonly TimeProvider timeProvider;

    public ForumService(
        DanielsDojoDbContext context,
        ICommunityAccessEvaluator accessEvaluator,
        TimeProvider timeProvider)
    {
        this.context = context;
        this.accessEvaluator = accessEvaluator;
        this.timeProvider = timeProvider;
    }

    // ------------------------------------------------------------------ reading

    public async Task<IReadOnlyList<ForumCategorySummary>> ListCategoriesAsync(
        CancellationToken cancellationToken = default) =>
        await context.ForumCategories
            .AsNoTracking()
            .Where(category => category.Status == ForumCategoryStatus.Active)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Take(MaxCategoryListSize)
            .Select(category => new ForumCategorySummary(
                category.Id,
                category.Slug,
                category.Name,
                category.Description,
                category.SortOrder,
                category.Threads.Count(thread => thread.Status != ForumThreadStatus.Removed),
                category.Threads
                    .Where(thread => thread.Status != ForumThreadStatus.Removed)
                    .Max(thread => (DateTimeOffset?)thread.LastActivityAtUtc)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ForumThreadSummary>> ListRecentThreadsAsync(
        Guid readerUserId,
        int count,
        CancellationToken cancellationToken = default)
    {
        int size = count < 1 ? 5 : Math.Min(count, MaxPageSize);
        HashSet<Guid> hidden = await HiddenAuthorsAsync(readerUserId, cancellationToken);

        var rows = await context.ForumThreads
            .AsNoTracking()
            .Where(thread => thread.Status != ForumThreadStatus.Removed
                && thread.Category!.Status == ForumCategoryStatus.Active)
            .OrderByDescending(thread => thread.LastActivityAtUtc)
            .ThenBy(thread => thread.Id)
            .Take(size)
            .Select(thread => new
            {
                thread.Id,
                thread.Title,
                CategorySlug = thread.Category!.Slug,
                thread.AuthorUserId,
                AuthorHandle = context.CommunityProfiles
                    .Where(profile => profile.UserId == thread.AuthorUserId)
                    .Select(profile => profile.Handle)
                    .FirstOrDefault(),
                thread.Status,
                thread.IsPinned,
                ReplyCount = thread.Posts.Count(post => post.Status != ForumPostStatus.Removed) - 1,
                thread.CreatedAtUtc,
                thread.LastActivityAtUtc,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row =>
            {
                bool authorHidden = hidden.Contains(row.AuthorUserId);

                return new ForumThreadSummary(
                    row.Id,
                    row.Title,
                    row.CategorySlug,
                    authorHidden ? "Hidden member" : row.AuthorHandle ?? "Former member",
                    authorHidden,
                    row.Status.ToString(),
                    row.IsPinned,
                    Math.Max(row.ReplyCount, 0),
                    row.CreatedAtUtc,
                    row.LastActivityAtUtc);
            })
            .ToList();
    }

    public async Task<OperationResult<PagedResult<ForumThreadSummary>>> ListThreadsAsync(
        Guid readerUserId,
        string categorySlug,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ForumCategory? category = await context.ForumCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Slug == categorySlug
                    && candidate.Status == ForumCategoryStatus.Active,
                cancellationToken);

        if (category is null)
        {
            return OperationResult.NotFound().ToFailure<PagedResult<ForumThreadSummary>>();
        }

        HashSet<Guid> hidden = await HiddenAuthorsAsync(readerUserId, cancellationToken);

        IQueryable<ForumThread> threads = context.ForumThreads
            .AsNoTracking()
            .Where(thread => thread.CategoryId == category.Id
                && thread.Status != ForumThreadStatus.Removed);

        int totalCount = await threads.CountAsync(cancellationToken);
        (int currentPage, int size) = Paging(page, pageSize);

        var rows = await threads
            .OrderByDescending(thread => thread.IsPinned)
            .ThenByDescending(thread => thread.LastActivityAtUtc)
            .ThenBy(thread => thread.Id)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .Select(thread => new
            {
                thread.Id,
                thread.Title,
                thread.AuthorUserId,
                AuthorHandle = context.CommunityProfiles
                    .Where(profile => profile.UserId == thread.AuthorUserId)
                    .Select(profile => profile.Handle)
                    .FirstOrDefault(),
                thread.Status,
                thread.IsPinned,
                ReplyCount = thread.Posts.Count(post => post.Status != ForumPostStatus.Removed) - 1,
                thread.CreatedAtUtc,
                thread.LastActivityAtUtc,
            })
            .ToListAsync(cancellationToken);

        List<ForumThreadSummary> items = rows
            .Select(row =>
            {
                bool authorHidden = hidden.Contains(row.AuthorUserId);

                return new ForumThreadSummary(
                    row.Id,
                    row.Title,
                    category.Slug,
                    authorHidden ? "Hidden member" : row.AuthorHandle ?? "Former member",
                    authorHidden,
                    row.Status.ToString(),
                    row.IsPinned,
                    Math.Max(row.ReplyCount, 0),
                    row.CreatedAtUtc,
                    row.LastActivityAtUtc);
            })
            .ToList();

        return OperationResult.FromValue(new PagedResult<ForumThreadSummary>(
            items,
            currentPage,
            size,
            totalCount,
            size == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)size)));
    }

    public async Task<OperationResult<ForumThreadDetail>> GetThreadAsync(
        Guid readerUserId,
        Guid threadId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        await BuildThreadAsync(readerUserId, threadId, page, pageSize, cancellationToken);

    // ------------------------------------------------------------------ writing

    public async Task<OperationResult<ForumThreadDetail>> CreateThreadAsync(
        Guid authorUserId,
        CreateThreadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult? denied = await RequireParticipationAsync(authorUserId, cancellationToken);

        if (denied is not null)
        {
            return denied.ToFailure<ForumThreadDetail>();
        }

        var validation = new ValidationBuilder()
            .Required("title", request.Title, MaxTitleLength, "Title")
            .Required("body", request.Body, MaxBodyLength, "Body");

        if (validation.HasErrors)
        {
            return validation.ToResult().ToFailure<ForumThreadDetail>();
        }

        ForumCategory? category = await context.ForumCategories.FirstOrDefaultAsync(
            candidate => candidate.Slug == request.CategorySlug
                && candidate.Status == ForumCategoryStatus.Active,
            cancellationToken);

        if (category is null)
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "categorySlug",
                "Choose a category that is open for new threads.").ToFailure<ForumThreadDetail>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var thread = new ForumThread
        {
            Id = Guid.CreateVersion7(),
            CategoryId = category.Id,
            AuthorUserId = authorUserId,
            Title = request.Title.Trim(),
            Status = ForumThreadStatus.Open,
            IsPinned = false,
            LastActivityAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.ForumThreads.Add(thread);
        context.ForumPosts.Add(new ForumPost
        {
            Id = Guid.CreateVersion7(),
            ThreadId = thread.Id,
            AuthorUserId = authorUserId,
            Body = request.Body.Trim(),
            Status = ForumPostStatus.Published,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        // The author follows their own thread, which is what most people expect and what
        // makes the reply notification useful at all.
        context.ForumSubscriptions.Add(new ForumSubscription
        {
            ThreadId = thread.Id,
            UserId = authorUserId,
            NotificationPreference = ThreadNotificationPreference.AllReplies,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        await context.SaveChangesAsync(cancellationToken);

        return await BuildThreadAsync(authorUserId, thread.Id, 1, DefaultPageSize, cancellationToken);
    }

    public async Task<OperationResult<ForumThreadDetail>> CreatePostAsync(
        Guid authorUserId,
        Guid threadId,
        CreatePostRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult? denied = await RequireParticipationAsync(authorUserId, cancellationToken);

        if (denied is not null)
        {
            return denied.ToFailure<ForumThreadDetail>();
        }

        var validation = new ValidationBuilder().Required("body", request.Body, MaxBodyLength, "Body");

        if (validation.HasErrors)
        {
            return validation.ToResult().ToFailure<ForumThreadDetail>();
        }

        ForumThread? thread = await context.ForumThreads
            .FirstOrDefaultAsync(candidate => candidate.Id == threadId, cancellationToken);

        if (thread is null || thread.Status == ForumThreadStatus.Removed)
        {
            return OperationResult.NotFound().ToFailure<ForumThreadDetail>();
        }

        if (!thread.AcceptsReplies)
        {
            return OperationResult.Conflict(
                ErrorCodes.CommunityForbidden,
                "This thread is not accepting replies.").ToFailure<ForumThreadDetail>();
        }

        if (request.ReplyToPostId is { } parentId
            && !await context.ForumPosts.AnyAsync(
                post => post.Id == parentId && post.ThreadId == threadId, cancellationToken))
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "replyToPostId",
                "That post is not part of this thread.").ToFailure<ForumThreadDetail>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var post = new ForumPost
        {
            Id = Guid.CreateVersion7(),
            ThreadId = threadId,
            AuthorUserId = authorUserId,
            ReplyToPostId = request.ReplyToPostId,
            Body = request.Body.Trim(),
            Status = ForumPostStatus.Published,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.ForumPosts.Add(post);
        thread.LastActivityAtUtc = now;
        thread.UpdatedAtUtc = now;

        await NotifySubscribersAsync(threadId, authorUserId, post.Id, now, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        return await BuildThreadAsync(authorUserId, threadId, LastPage(threadId), DefaultPageSize, cancellationToken);
    }

    public async Task<OperationResult<ForumThreadDetail>> UpdatePostAsync(
        Guid authorUserId,
        Guid postId,
        UpdatePostRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult? denied = await RequireParticipationAsync(authorUserId, cancellationToken);

        if (denied is not null)
        {
            return denied.ToFailure<ForumThreadDetail>();
        }

        ForumPost? post = await context.ForumPosts
            .FirstOrDefaultAsync(candidate => candidate.Id == postId, cancellationToken);

        // Someone else's post is reported as missing rather than forbidden: the caller has no
        // business learning which post identifiers exist.
        if (post is null || post.AuthorUserId != authorUserId)
        {
            return OperationResult.NotFound().ToFailure<ForumThreadDetail>();
        }

        if (post.Status == ForumPostStatus.Removed)
        {
            return OperationResult.Conflict(
                ErrorCodes.CommunityForbidden,
                "This post was removed and can no longer be edited.").ToFailure<ForumThreadDetail>();
        }

        var validation = new ValidationBuilder().Required("body", request.Body, MaxBodyLength, "Body");

        if (validation.HasErrors)
        {
            return validation.ToResult().ToFailure<ForumThreadDetail>();
        }

        if (!RowVersionToken.TryDecode(request.RowVersion, out byte[] rowVersion))
        {
            return OperationResult.Invalid(
                ErrorCodes.InvalidRowVersion,
                "rowVersion",
                "The supplied version token is not valid. Reload and try again.")
                .ToFailure<ForumThreadDetail>();
        }

        context.Entry(post).Property(nameof(ForumPost.RowVersion)).OriginalValue = rowVersion;

        DateTimeOffset now = timeProvider.GetUtcNow();
        post.Body = request.Body.Trim();
        post.Status = ForumPostStatus.Edited;
        post.EditedAtUtc = now;
        post.UpdatedAtUtc = now;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return OperationResult.ConcurrencyConflict().ToFailure<ForumThreadDetail>();
        }

        return await BuildThreadAsync(authorUserId, post.ThreadId, 1, DefaultPageSize, cancellationToken);
    }

    public async Task<OperationResult<ForumThreadDetail>> RemoveOwnPostAsync(
        Guid authorUserId,
        Guid postId,
        CancellationToken cancellationToken = default)
    {
        ForumPost? post = await context.ForumPosts
            .FirstOrDefaultAsync(candidate => candidate.Id == postId, cancellationToken);

        if (post is null || post.AuthorUserId != authorUserId)
        {
            return OperationResult.NotFound().ToFailure<ForumThreadDetail>();
        }

        if (post.Status != ForumPostStatus.Removed)
        {
            Tombstone(post, timeProvider.GetUtcNow());
            await context.SaveChangesAsync(cancellationToken);
        }

        return await BuildThreadAsync(authorUserId, post.ThreadId, 1, DefaultPageSize, cancellationToken);
    }

    public async Task<OperationResult<ForumThreadDetail>> SetReactionAsync(
        Guid userId,
        Guid postId,
        bool liked,
        CancellationToken cancellationToken = default)
    {
        OperationResult? denied = await RequireParticipationAsync(userId, cancellationToken);

        if (denied is not null)
        {
            return denied.ToFailure<ForumThreadDetail>();
        }

        ForumPost? post = await context.ForumPosts
            .FirstOrDefaultAsync(candidate => candidate.Id == postId, cancellationToken);

        if (post is null || post.Status == ForumPostStatus.Removed)
        {
            return OperationResult.NotFound().ToFailure<ForumThreadDetail>();
        }

        ForumPostReaction? existing = await context.ForumPostReactions.FirstOrDefaultAsync(
            reaction => reaction.PostId == postId
                && reaction.UserId == userId
                && reaction.ReactionType == ReactionType.Like,
            cancellationToken);

        DateTimeOffset now = timeProvider.GetUtcNow();

        if (liked && existing is null)
        {
            context.ForumPostReactions.Add(new ForumPostReaction
            {
                Id = Guid.CreateVersion7(),
                PostId = postId,
                UserId = userId,
                ReactionType = ReactionType.Like,
                CreatedAtUtc = now,
            });

            if (post.AuthorUserId != userId
                && !await IsBlockedEitherWayAsync(userId, post.AuthorUserId, cancellationToken))
            {
                AddNotification(post.AuthorUserId, userId, NotificationKind.PostReaction, "Post", postId, now);
            }
        }
        else if (!liked && existing is not null)
        {
            context.ForumPostReactions.Remove(existing);
        }

        await context.SaveChangesAsync(cancellationToken);

        return await BuildThreadAsync(userId, post.ThreadId, 1, DefaultPageSize, cancellationToken);
    }

    public async Task<OperationResult<ForumThreadDetail>> SetSubscriptionAsync(
        Guid userId,
        Guid threadId,
        bool subscribed,
        CancellationToken cancellationToken = default)
    {
        OperationResult? denied = await RequireParticipationAsync(userId, cancellationToken);

        if (denied is not null)
        {
            return denied.ToFailure<ForumThreadDetail>();
        }

        if (!await context.ForumThreads.AnyAsync(
                thread => thread.Id == threadId && thread.Status != ForumThreadStatus.Removed,
                cancellationToken))
        {
            return OperationResult.NotFound().ToFailure<ForumThreadDetail>();
        }

        ForumSubscription? existing = await context.ForumSubscriptions.FirstOrDefaultAsync(
            subscription => subscription.ThreadId == threadId && subscription.UserId == userId,
            cancellationToken);

        DateTimeOffset now = timeProvider.GetUtcNow();

        if (subscribed && existing is null)
        {
            context.ForumSubscriptions.Add(new ForumSubscription
            {
                ThreadId = threadId,
                UserId = userId,
                NotificationPreference = ThreadNotificationPreference.AllReplies,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }
        else if (!subscribed && existing is not null)
        {
            context.ForumSubscriptions.Remove(existing);
        }

        await context.SaveChangesAsync(cancellationToken);

        return await BuildThreadAsync(userId, threadId, 1, DefaultPageSize, cancellationToken);
    }

    public async Task<OperationResult> ReportAsync(
        Guid reporterUserId,
        CreateReportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult? denied = await RequireParticipationAsync(reporterUserId, cancellationToken);

        if (denied is not null)
        {
            return denied;
        }

        var validation = new ValidationBuilder()
            .When(
                !Enum.TryParse(request.TargetType, ignoreCase: true, out ReportTargetType _),
                "targetType",
                "Choose what you are reporting.")
            .When(
                !Enum.TryParse(request.ReasonCode, ignoreCase: true, out ReportReasonCode _),
                "reasonCode",
                "Choose a reason.")
            .Optional("detail", request.Detail, 1000, "Detail");

        if (validation.HasErrors)
        {
            return validation.ToResult();
        }

        var targetType = Enum.Parse<ReportTargetType>(request.TargetType, ignoreCase: true);

        if (!await TargetExistsAsync(targetType, request.TargetId, cancellationToken))
        {
            return OperationResult.NotFound();
        }

        // One open report per reporter per target. The filtered unique index enforces this
        // too, so a double submission cannot flood the queue even under a race.
        if (await context.Reports.AnyAsync(
                report => report.ReporterUserId == reporterUserId
                    && report.TargetType == targetType
                    && report.TargetId == request.TargetId
                    && (report.Status == ReportStatus.Open || report.Status == ReportStatus.Reviewing),
                cancellationToken))
        {
            return OperationResult.Conflict(
                ErrorCodes.DuplicateValue,
                "You have already reported this. A moderator is looking at it.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var report = new Report
        {
            Id = Guid.CreateVersion7(),
            ReporterUserId = reporterUserId,
            TargetType = targetType,
            TargetId = request.TargetId,
            ReasonCode = Enum.Parse<ReportReasonCode>(request.ReasonCode, ignoreCase: true),
            Detail = string.IsNullOrWhiteSpace(request.Detail) ? null : request.Detail.Trim(),
            Status = ReportStatus.Open,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.Reports.Add(report);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            return OperationResult.Conflict(
                ErrorCodes.DuplicateValue,
                "You have already reported this. A moderator is looking at it.");
        }

        return OperationResult.Success();
    }

    // ------------------------------------------------------------------ shared helpers

    /// <summary>Clears a body and records the tombstone, keeping the row for continuity.</summary>
    internal static void Tombstone(ForumPost post, DateTimeOffset now)
    {
        post.Body = string.Empty;
        post.Status = ForumPostStatus.Removed;
        post.RemovedAtUtc = now;
        post.UpdatedAtUtc = now;
    }

    private async Task<OperationResult?> RequireParticipationAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        CommunityAccess access = await accessEvaluator.EvaluateAsync(userId, cancellationToken);

        if (access.Granted)
        {
            return null;
        }

        return OperationResult.Forbidden(
            access.Denial == CommunityAccessDenial.SetupRequired
                ? ErrorCodes.CommunitySetupRequired
                : ErrorCodes.CommunityForbidden,
            access.Message ?? "You cannot take part in the community right now.");
    }

    /// <summary>Members whose content this reader must not see, in either block direction.</summary>
    private async Task<HashSet<Guid>> HiddenAuthorsAsync(Guid readerUserId, CancellationToken cancellationToken)
    {
        List<Guid> blocked = await context.UserBlocks
            .AsNoTracking()
            .Where(block => block.BlockerUserId == readerUserId || block.BlockedUserId == readerUserId)
            .Select(block => block.BlockerUserId == readerUserId ? block.BlockedUserId : block.BlockerUserId)
            .ToListAsync(cancellationToken);

        return [.. blocked];
    }

    private Task<bool> IsBlockedEitherWayAsync(Guid first, Guid second, CancellationToken cancellationToken) =>
        context.UserBlocks.AnyAsync(
            block => (block.BlockerUserId == first && block.BlockedUserId == second)
                || (block.BlockerUserId == second && block.BlockedUserId == first),
            cancellationToken);

    private Task<bool> TargetExistsAsync(
        ReportTargetType targetType,
        Guid targetId,
        CancellationToken cancellationToken) => targetType switch
        {
            ReportTargetType.Profile =>
                context.CommunityProfiles.AnyAsync(profile => profile.UserId == targetId, cancellationToken),
            ReportTargetType.Thread =>
                context.ForumThreads.AnyAsync(thread => thread.Id == targetId, cancellationToken),
            ReportTargetType.Post =>
                context.ForumPosts.AnyAsync(post => post.Id == targetId, cancellationToken),
            _ =>
                context.DirectMessages.AnyAsync(message => message.Id == targetId, cancellationToken),
        };

    /// <summary>Queues a reply notification for every subscriber except the author.</summary>
    private async Task NotifySubscribersAsync(
        Guid threadId,
        Guid authorUserId,
        Guid postId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<Guid> subscribers = await context.ForumSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.ThreadId == threadId
                && subscription.UserId != authorUserId
                && subscription.NotificationPreference == ThreadNotificationPreference.AllReplies)
            .Select(subscription => subscription.UserId)
            .ToListAsync(cancellationToken);

        if (subscribers.Count == 0)
        {
            return;
        }

        // A block silences the notification in both directions, so neither member is pulled
        // back towards someone they have stepped away from.
        HashSet<Guid> blocked = [.. await context.UserBlocks
            .AsNoTracking()
            .Where(block => block.BlockerUserId == authorUserId || block.BlockedUserId == authorUserId)
            .Select(block => block.BlockerUserId == authorUserId ? block.BlockedUserId : block.BlockerUserId)
            .ToListAsync(cancellationToken)];

        foreach (Guid subscriber in subscribers.Where(id => !blocked.Contains(id)))
        {
            AddNotification(subscriber, authorUserId, NotificationKind.ThreadReply, "Post", postId, now);
        }
    }

    private void AddNotification(
        Guid recipientUserId,
        Guid actorUserId,
        NotificationKind kind,
        string targetType,
        Guid targetId,
        DateTimeOffset now) =>
        context.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(),
            RecipientUserId = recipientUserId,
            ActorUserId = actorUserId,
            Kind = kind,
            TargetType = targetType,
            TargetId = targetId,
            CreatedAtUtc = now,
        });

    private int LastPage(Guid threadId)
    {
        int posts = context.ForumPosts.Count(post => post.ThreadId == threadId);

        return Math.Max(1, (int)Math.Ceiling(posts / (double)DefaultPageSize));
    }

    private static Dictionary<string, string> ThreadMetadata(Guid threadId) =>
        new(StringComparer.Ordinal) { ["threadId"] = threadId.ToString("D") };

    private static (int Page, int PageSize) Paging(int page, int pageSize) => (
        page < 1 ? 1 : page,
        pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize));

    /// <summary>Builds the thread view for one reader, applying blocks and ownership.</summary>
    private async Task<OperationResult<ForumThreadDetail>> BuildThreadAsync(
        Guid readerUserId,
        Guid threadId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();

        var thread = await context.ForumThreads
            .AsNoTracking()
            .Where(candidate => candidate.Id == threadId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Title,
                CategorySlug = candidate.Category!.Slug,
                CategoryName = candidate.Category.Name,
                candidate.AuthorUserId,
                AuthorHandle = context.CommunityProfiles
                    .Where(profile => profile.UserId == candidate.AuthorUserId)
                    .Select(profile => profile.Handle)
                    .FirstOrDefault(),
                candidate.Status,
                candidate.IsPinned,
                candidate.CreatedAtUtc,
                candidate.LastActivityAtUtc,
                candidate.RowVersion,
            })
            .FirstOrDefaultAsync(cancellationToken);

        // A removed thread is indistinguishable from one that never existed.
        if (thread is null || thread.Status == ForumThreadStatus.Removed)
        {
            return OperationResult.NotFound().ToFailure<ForumThreadDetail>();
        }

        HashSet<Guid> hidden = await HiddenAuthorsAsync(readerUserId, cancellationToken);
        (int currentPage, int size) = Paging(page, pageSize);

        IQueryable<ForumPost> posts = context.ForumPosts
            .AsNoTracking()
            .Where(post => post.ThreadId == threadId);

        int totalCount = await posts.CountAsync(cancellationToken);

        var rows = await posts
            .OrderBy(post => post.CreatedAtUtc)
            .ThenBy(post => post.Id)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .Select(post => new
            {
                post.Id,
                post.ReplyToPostId,
                post.AuthorUserId,
                AuthorHandle = context.CommunityProfiles
                    .Where(profile => profile.UserId == post.AuthorUserId)
                    .Select(profile => profile.Handle)
                    .FirstOrDefault(),
                post.Body,
                post.Status,
                LikeCount = post.Reactions.Count,
                LikedByMe = post.Reactions.Any(reaction => reaction.UserId == readerUserId),
                post.CreatedAtUtc,
                post.EditedAtUtc,
                post.RowVersion,
            })
            .ToListAsync(cancellationToken);

        List<ForumPostView> views = rows
            .Select(row =>
            {
                bool authorHidden = hidden.Contains(row.AuthorUserId);
                bool removed = row.Status == ForumPostStatus.Removed;
                bool withheld = authorHidden || removed;

                return new ForumPostView(
                    row.Id,
                    row.ReplyToPostId,
                    authorHidden ? "Hidden member" : row.AuthorHandle ?? "Former member",
                    authorHidden,
                    row.AuthorUserId == readerUserId,

                    // Withheld bodies are never serialised, so nothing is hidden client-side.
                    withheld ? string.Empty : row.Body,
                    row.Status.ToString(),
                    withheld,
                    removed ? "This post was removed." : authorHidden ? "Hidden because of a block." : null,
                    row.LikeCount,
                    row.LikedByMe,
                    row.CreatedAtUtc,
                    row.EditedAtUtc,
                    RowVersionToken.Encode(row.RowVersion));
            })
            .ToList();

        bool subscribed = await context.ForumSubscriptions.AnyAsync(
            subscription => subscription.ThreadId == threadId && subscription.UserId == readerUserId,
            cancellationToken);

        bool authorBlocked = hidden.Contains(thread.AuthorUserId);

        return OperationResult.FromValue(new ForumThreadDetail(
            thread.Id,
            thread.Title,
            thread.CategorySlug,
            thread.CategoryName,
            authorBlocked ? "Hidden member" : thread.AuthorHandle ?? "Former member",
            thread.Status.ToString(),
            thread.IsPinned,
            thread.Status == ForumThreadStatus.Open,
            subscribed,
            thread.CreatedAtUtc,
            thread.LastActivityAtUtc,
            new PagedResult<ForumPostView>(
                views,
                currentPage,
                size,
                totalCount,
                size == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)size)),
            RowVersionToken.Encode(thread.RowVersion)));
    }
}
