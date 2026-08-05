using DanielsDojo.Application.Catalog;
using DanielsDojo.Application.Common;
using DanielsDojo.Application.Community;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Domain.Community;
using DanielsDojo.Infrastructure.Auditing;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DanielsDojo.Infrastructure.Community;

/// <summary>
/// Moderator actions over community content and profiles.
/// </summary>
/// <remarks>
/// Nothing here deletes. A removed post keeps its row with an empty body, a removed thread
/// keeps its posts, and a suspended profile keeps everything the member wrote. That is what
/// makes a decision reviewable later and what stops a moderation action from silently
/// rewriting a conversation other people took part in.
/// </remarks>
internal sealed class ModerationService : IModerationService
{
    private const int MaxReasonLength = 512;
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    /// <summary>Hard ceiling on the category list, which has no paging of its own.</summary>
    private const int MaxCategoryListSize = 100;

    /// <summary>
    /// Recent privileged actions shown on the Admin landing page. Small on purpose: this is a
    /// "what just happened" strip, not a searchable log.
    /// </summary>
    private const int RecentActivityCount = 8;

    private readonly DanielsDojoDbContext context;
    private readonly TimeProvider timeProvider;
    private readonly AuditTrail audit;

    public ModerationService(
        DanielsDojoDbContext context,
        IOperationContext operationContext,
        TimeProvider timeProvider)
    {
        this.context = context;
        this.timeProvider = timeProvider;
        audit = new AuditTrail(context, operationContext, timeProvider);
    }

    public async Task<PagedResult<ModerationReport>> ListReportsAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Report> reports = context.Reports.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            reports = Enum.TryParse(status, ignoreCase: true, out ReportStatus parsed)
                ? reports.Where(report => report.Status == parsed)
                : reports.Where(static _ => false);
        }

        int totalCount = await reports.CountAsync(cancellationToken);
        (int currentPage, int size) = Paging(page, pageSize);

        List<ModerationReport> items = await reports
            .OrderByDescending(report => report.CreatedAtUtc)
            .ThenBy(report => report.Id)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .Select(report => new ModerationReport(
                report.Id,
                report.TargetType.ToString(),
                report.TargetId,
                report.ReasonCode.ToString(),
                report.Detail,
                report.Status.ToString(),
                context.CommunityProfiles
                    .Where(profile => profile.UserId == report.ReporterUserId)
                    .Select(profile => profile.Handle)
                    .FirstOrDefault() ?? "Former member",
                report.HandledByUser!.DisplayName,
                report.Resolution,
                report.CreatedAtUtc,
                report.HandledAtUtc,
                RowVersionToken.Encode(report.RowVersion)))
            .ToListAsync(cancellationToken);

        return new PagedResult<ModerationReport>(
            items,
            currentPage,
            size,
            totalCount,
            size == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)size));
    }

    public async Task<AdminOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        int draft = await context.Courses
            .CountAsync(course => course.Status == PublicationStatus.Draft, cancellationToken);
        int published = await context.Courses
            .CountAsync(course => course.Status == PublicationStatus.Published, cancellationToken);
        int archived = await context.Courses
            .CountAsync(course => course.Status == PublicationStatus.Archived, cancellationToken);

        // "Ready to publish" applies the same prerequisite the publish command enforces: a
        // published section holding a published lesson. Showing it saves an operator from
        // opening every draft to find out which ones are actually finishable.
        int ready = await context.Courses.CountAsync(
            course => course.Status == PublicationStatus.Draft
                && course.Sections.Any(section => section.Status == PublicationStatus.Published
                    && section.Lessons.Any(lesson => lesson.Status == PublicationStatus.Published)),
            cancellationToken);

        int activeOffers = await context.Offers
            .CountAsync(offer => offer.Status == CommerceStatus.Active, cancellationToken);
        int draftOffers = await context.Offers
            .CountAsync(offer => offer.Status == CommerceStatus.Draft, cancellationToken);
        int openReports = await context.Reports
            .CountAsync(report => report.Status == ReportStatus.Open, cancellationToken);
        int reviewing = await context.Reports
            .CountAsync(report => report.Status == ReportStatus.Reviewing, cancellationToken);
        int categories = await context.ForumCategories
            .CountAsync(category => category.Status == ForumCategoryStatus.Active, cancellationToken);

        DateTimeOffset thirtyDaysAgo = timeProvider.GetUtcNow().AddDays(-30);

        int totalUsers = await context.Users.CountAsync(cancellationToken);
        int newUsers = await context.Users
            .CountAsync(user => user.CreatedAtUtc >= thirtyDaysAgo, cancellationToken);
        int activeMemberships = await context.Subscriptions
            .CountAsync(s2 => s2.Status == SubscriptionStatus.Active, cancellationToken);
        int enrollments = await context.Enrollments.CountAsync(cancellationToken);
        int certificates = await context.Certificates
            .CountAsync(c => c.RevokedAtUtc == null, cancellationToken);

        int paidOrders = await context.Orders
            .CountAsync(order => order.Status == OrderStatus.Paid, cancellationToken);

        // Revenue is the sum of genuinely paid orders — never estimates, never pending ones.
        long revenueMinor = await context.Orders
            .Where(order => order.Status == OrderStatus.Paid)
            .SumAsync(order => (long?)order.TotalMinor, cancellationToken) ?? 0;

        int videosReady = await context.LessonVideos
            .CountAsync(v => v.Status == LessonVideoStatus.Ready, cancellationToken);
        int videosProcessing = await context.LessonVideos.CountAsync(
            v => v.Status == LessonVideoStatus.MuxIngesting
                || v.Status == LessonVideoStatus.Processing
                || v.Status == LessonVideoStatus.Replacing,
            cancellationToken);
        int videosFailed = await context.LessonVideos
            .CountAsync(v => v.Status == LessonVideoStatus.Failed, cancellationToken);

        List<AuditActivityEntry> recent = await context.AuditLogs
            .AsNoTracking()
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Take(RecentActivityCount)
            .Select(entry => new AuditActivityEntry(
                entry.Id,
                entry.Action,
                entry.TargetType,
                entry.TargetId,
                context.Users
                    .Where(user => user.Id == entry.ActorUserId)
                    .Select(user => user.DisplayName)
                    .FirstOrDefault() ?? "System",
                entry.Reason,
                entry.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        return new AdminOverview(
            draft,
            published,
            archived,
            ready,
            activeOffers,
            draftOffers,
            openReports,
            reviewing,
            categories,
            totalUsers,
            newUsers,
            activeMemberships,
            enrollments,
            certificates,
            paidOrders,
            revenueMinor,
            videosReady,
            videosProcessing,
            videosFailed,
            recent);
    }

    public async Task<IReadOnlyList<AdminForumCategory>> ListCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await context.ForumCategories
            .AsNoTracking()
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Take(MaxCategoryListSize)
            .Select(category => new
            {
                category.Id,
                category.Slug,
                category.Name,
                category.Description,
                category.SortOrder,
                category.Status,
                ThreadCount = category.Threads.Count(thread => thread.Status != ForumThreadStatus.Removed),
                category.RowVersion,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new AdminForumCategory(
                row.Id,
                row.Slug,
                row.Name,
                row.Description,
                row.SortOrder,
                row.Status.ToString(),
                row.ThreadCount,
                RowVersionToken.Encode(row.RowVersion)))
            .ToList();
    }

    public async Task<OperationResult<AdminForumCategory>> CreateCategoryAsync(
        CreateForumCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = new ValidationBuilder()
            .When(!CatalogSlug.IsValid(request.Slug?.Trim()), "slug", CatalogSlug.Requirement)
            .Required("name", request.Name, 100, "Name")
            .Required("description", request.Description, 500, "Description")
            .When(request.SortOrder < 0, "sortOrder", "Position cannot be negative.");

        if (validation.HasErrors)
        {
            return validation.ToResult().ToFailure<AdminForumCategory>();
        }

        string slug = request.Slug!.Trim();

        if (await context.ForumCategories.AnyAsync(
                category => category.Slug == slug, cancellationToken))
        {
            return OperationResult.Invalid(
                ErrorCodes.DuplicateValue,
                "slug",
                "Another category already uses this slug.").ToFailure<AdminForumCategory>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var created = new ForumCategory
        {
            Id = Guid.CreateVersion7(),
            Slug = slug,
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            SortOrder = request.SortOrder,
            Status = ForumCategoryStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.ForumCategories.Add(created);
        audit.Append(
            "Community.Category.Created",
            nameof(ForumCategory),
            created.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["slug"] = created.Slug });

        await context.SaveChangesAsync(cancellationToken);

        return await ReloadCategoryAsync(created.Id, cancellationToken);
    }

    public async Task<OperationResult<AdminForumCategory>> UpdateCategoryAsync(
        Guid categoryId,
        UpdateForumCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ForumCategory? category = await context.ForumCategories
            .FirstOrDefaultAsync(candidate => candidate.Id == categoryId, cancellationToken);

        if (category is null)
        {
            return OperationResult.NotFound().ToFailure<AdminForumCategory>();
        }

        var validation = new ValidationBuilder()
            .Required("name", request.Name, 100, "Name")
            .Required("description", request.Description, 500, "Description")
            .When(request.SortOrder < 0, "sortOrder", "Position cannot be negative.");

        if (validation.HasErrors)
        {
            return validation.ToResult().ToFailure<AdminForumCategory>();
        }

        if (!RowVersionToken.TryDecode(request.RowVersion, out byte[] rowVersion))
        {
            return InvalidCategoryRowVersion();
        }

        context.Entry(category).Property(nameof(ForumCategory.RowVersion)).OriginalValue = rowVersion;

        category.Name = request.Name.Trim();
        category.Description = request.Description.Trim();
        category.SortOrder = request.SortOrder;
        category.UpdatedAtUtc = timeProvider.GetUtcNow();

        audit.Append(
            "Community.Category.Updated",
            nameof(ForumCategory),
            category.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["slug"] = category.Slug });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return OperationResult.ConcurrencyConflict().ToFailure<AdminForumCategory>();
        }

        return await ReloadCategoryAsync(categoryId, cancellationToken);
    }

    public async Task<OperationResult<AdminForumCategory>> SetCategoryStatusAsync(
        Guid categoryId,
        string targetStatus,
        ModerationDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ForumCategory? category = await context.ForumCategories
            .FirstOrDefaultAsync(candidate => candidate.Id == categoryId, cancellationToken);

        if (category is null)
        {
            return OperationResult.NotFound().ToFailure<AdminForumCategory>();
        }

        OperationResult? invalid = ValidateReason(request.Reason);

        if (invalid is not null)
        {
            return invalid.ToFailure<AdminForumCategory>();
        }

        if (!Enum.TryParse(targetStatus, ignoreCase: true, out ForumCategoryStatus target))
        {
            return OperationResult.Invalid(ErrorCodes.ValidationFailed, "status", "Unknown status.")
                .ToFailure<AdminForumCategory>();
        }

        if (!RowVersionToken.TryDecode(request.RowVersion, out byte[] rowVersion))
        {
            return InvalidCategoryRowVersion();
        }

        context.Entry(category).Property(nameof(ForumCategory.RowVersion)).OriginalValue = rowVersion;

        ForumCategoryStatus previous = category.Status;
        category.Status = target;
        category.UpdatedAtUtc = timeProvider.GetUtcNow();

        // Archiving hides a category from ordinary listings. Its threads are never touched,
        // so nothing anyone wrote disappears because a category was tidied away.
        audit.Append(
            "Community.Category.StatusChanged",
            nameof(ForumCategory),
            category.Id,
            request.Reason,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["slug"] = category.Slug,
                ["previousStatus"] = previous.ToString(),
                ["status"] = target.ToString(),
            });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return OperationResult.ConcurrencyConflict().ToFailure<AdminForumCategory>();
        }

        return await ReloadCategoryAsync(categoryId, cancellationToken);
    }

    private static OperationResult<AdminForumCategory> InvalidCategoryRowVersion() =>
        OperationResult.Invalid(
            ErrorCodes.InvalidRowVersion,
            "rowVersion",
            "The supplied version token is not valid. Reload and try again.")
            .ToFailure<AdminForumCategory>();

    private async Task<OperationResult<AdminForumCategory>> ReloadCategoryAsync(
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();

        AdminForumCategory? reloaded = (await ListCategoriesAsync(cancellationToken))
            .FirstOrDefault(category => category.Id == categoryId);

        return reloaded is null
            ? OperationResult.NotFound().ToFailure<AdminForumCategory>()
            : OperationResult.FromValue(reloaded);
    }

    public async Task<OperationResult<ModerationTarget>> GetReportTargetAsync(
        Guid moderatorUserId,
        Guid reportId,
        CancellationToken cancellationToken = default)
    {
        Report? report = await context.Reports
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == reportId, cancellationToken);

        // Only an open report unlocks the content. Once a decision has been recorded there is
        // no longer a reason to keep re-reading someone's private message.
        if (report is null || !report.IsOpen)
        {
            return OperationResult.NotFound().ToFailure<ModerationTarget>();
        }

        ModerationTarget? target = report.TargetType switch
        {
            ReportTargetType.Post => await LoadPostTargetAsync(report, cancellationToken),
            ReportTargetType.Thread => await LoadThreadTargetAsync(report, cancellationToken),
            ReportTargetType.Message => await LoadMessageTargetAsync(report, cancellationToken),
            _ => await LoadProfileTargetAsync(report, cancellationToken),
        };

        if (target is null)
        {
            return OperationResult.NotFound().ToFailure<ModerationTarget>();
        }

        // Reading is itself a privileged action, so it leaves a trace naming what was opened —
        // and, deliberately, none of what it said.
        audit.Append(
            "Community.Report.TargetViewed",
            nameof(Report),
            report.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["targetType"] = report.TargetType.ToString(),
                ["targetId"] = report.TargetId.ToString("D"),
            });

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.FromValue(target);
    }

    public async Task<OperationResult<ModerationReport>> DecideReportAsync(
        Guid moderatorUserId,
        Guid reportId,
        string targetStatus,
        ModerationDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Report? report = await context.Reports
            .FirstOrDefaultAsync(candidate => candidate.Id == reportId, cancellationToken);

        if (report is null)
        {
            return OperationResult.NotFound().ToFailure<ModerationReport>();
        }

        OperationResult? invalid = ValidateReason(request.Reason);

        if (invalid is not null)
        {
            return invalid.ToFailure<ModerationReport>();
        }

        if (!Enum.TryParse(targetStatus, ignoreCase: true, out ReportStatus target)
            || !CanTransition(report.Status, target))
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "status",
                $"A {report.Status} report cannot move to {targetStatus}.").ToFailure<ModerationReport>();
        }

        if (!RowVersionToken.TryDecode(request.RowVersion, out byte[] rowVersion))
        {
            return OperationResult.Invalid(
                ErrorCodes.InvalidRowVersion,
                "rowVersion",
                "The supplied version token is not valid. Reload and try again.")
                .ToFailure<ModerationReport>();
        }

        context.Entry(report).Property(nameof(Report.RowVersion)).OriginalValue = rowVersion;

        ReportStatus previous = report.Status;
        DateTimeOffset now = timeProvider.GetUtcNow();

        report.Status = target;
        report.HandledByUserId = moderatorUserId;
        report.Resolution = request.Reason.Trim();
        report.UpdatedAtUtc = now;
        report.HandledAtUtc = target is ReportStatus.Resolved or ReportStatus.Dismissed ? now : null;

        audit.Append(
            "Community.Report.Decided",
            nameof(Report),
            report.Id,
            request.Reason,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["previousStatus"] = previous.ToString(),
                ["status"] = target.ToString(),
                ["targetType"] = report.TargetType.ToString(),
                ["targetId"] = report.TargetId.ToString("D"),
            });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return OperationResult.ConcurrencyConflict().ToFailure<ModerationReport>();
        }

        PagedResult<ModerationReport> refreshed =
            await ListReportsAsync(target.ToString(), 1, MaxPageSize, cancellationToken);

        ModerationReport? updated = refreshed.Items.FirstOrDefault(entry => entry.Id == reportId);

        return updated is null
            ? OperationResult.NotFound().ToFailure<ModerationReport>()
            : OperationResult.FromValue(updated);
    }

    public async Task<OperationResult> RemovePostAsync(
        Guid moderatorUserId,
        Guid postId,
        ModerationActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult? invalid = ValidateReason(request.Reason);

        if (invalid is not null)
        {
            return invalid;
        }

        ForumPost? post = await context.ForumPosts
            .FirstOrDefaultAsync(candidate => candidate.Id == postId, cancellationToken);

        if (post is null)
        {
            return OperationResult.NotFound();
        }

        if (post.Status == ForumPostStatus.Removed)
        {
            return OperationResult.Success();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        ForumService.Tombstone(post, now);

        NotifyMember(post.AuthorUserId, moderatorUserId, "Post", post.Id, now);
        audit.Append(
            "Community.Post.RemovedByModerator",
            nameof(ForumPost),
            post.Id,
            request.Reason,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["threadId"] = post.ThreadId.ToString("D"),
                ["authorUserId"] = post.AuthorUserId.ToString("D"),
            });

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    public async Task<OperationResult> SetThreadStatusAsync(
        Guid moderatorUserId,
        Guid threadId,
        string targetStatus,
        ModerationActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult? invalid = ValidateReason(request.Reason);

        if (invalid is not null)
        {
            return invalid;
        }

        if (!Enum.TryParse(targetStatus, ignoreCase: true, out ForumThreadStatus target))
        {
            return OperationResult.Invalid(ErrorCodes.ValidationFailed, "status", "Unknown status.");
        }

        ForumThread? thread = await context.ForumThreads
            .FirstOrDefaultAsync(candidate => candidate.Id == threadId, cancellationToken);

        if (thread is null)
        {
            return OperationResult.NotFound();
        }

        ForumThreadStatus previous = thread.Status;
        DateTimeOffset now = timeProvider.GetUtcNow();

        thread.Status = target;
        thread.UpdatedAtUtc = now;

        NotifyMember(thread.AuthorUserId, moderatorUserId, "Thread", thread.Id, now);
        audit.Append(
            "Community.Thread.StatusChanged",
            nameof(ForumThread),
            thread.Id,
            request.Reason,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["previousStatus"] = previous.ToString(),
                ["status"] = target.ToString(),
            });

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    public async Task<OperationResult> SetThreadPinnedAsync(
        Guid moderatorUserId,
        Guid threadId,
        bool pinned,
        ModerationActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult? invalid = ValidateReason(request.Reason);

        if (invalid is not null)
        {
            return invalid;
        }

        ForumThread? thread = await context.ForumThreads
            .FirstOrDefaultAsync(candidate => candidate.Id == threadId, cancellationToken);

        if (thread is null)
        {
            return OperationResult.NotFound();
        }

        thread.IsPinned = pinned;
        thread.UpdatedAtUtc = timeProvider.GetUtcNow();

        audit.Append(
            "Community.Thread.PinChanged",
            nameof(ForumThread),
            thread.Id,
            request.Reason,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["isPinned"] = pinned ? "true" : "false",
            });

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    public async Task<OperationResult> SetProfileStatusAsync(
        Guid moderatorUserId,
        Guid targetUserId,
        string targetStatus,
        ModerationActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult? invalid = ValidateReason(request.Reason);

        if (invalid is not null)
        {
            return invalid;
        }

        if (!Enum.TryParse(targetStatus, ignoreCase: true, out CommunityProfileStatus target))
        {
            return OperationResult.Invalid(ErrorCodes.ValidationFailed, "status", "Unknown status.");
        }

        CommunityProfile? profile = await context.CommunityProfiles
            .FirstOrDefaultAsync(candidate => candidate.UserId == targetUserId, cancellationToken);

        if (profile is null)
        {
            return OperationResult.NotFound();
        }

        CommunityProfileStatus previous = profile.Status;
        DateTimeOffset now = timeProvider.GetUtcNow();

        profile.Status = target;
        profile.UpdatedAtUtc = now;

        NotifyMember(targetUserId, moderatorUserId, "Profile", targetUserId, now);
        audit.Append(
            "Community.Profile.StatusChanged",
            nameof(CommunityProfile),
            targetUserId,
            request.Reason,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["previousStatus"] = previous.ToString(),
                ["status"] = target.ToString(),
            });

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    private async Task<ModerationTarget?> LoadPostTargetAsync(
        Report report,
        CancellationToken cancellationToken)
    {
        var row = await context.ForumPosts
            .AsNoTracking()
            .Where(post => post.Id == report.TargetId)
            .Select(post => new
            {
                post.Body,
                post.Status,
                post.CreatedAtUtc,
                post.AuthorUserId,
                ThreadTitle = post.Thread!.Title,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new ModerationTarget(
                report.Id,
                report.TargetType.ToString(),
                report.TargetId,
                await HandleAsync(row.AuthorUserId, cancellationToken),
                row.Status.ToString(),
                row.Body,
                $"In the thread \"{row.ThreadTitle}\".",
                row.CreatedAtUtc);
    }

    private async Task<ModerationTarget?> LoadThreadTargetAsync(
        Report report,
        CancellationToken cancellationToken)
    {
        var row = await context.ForumThreads
            .AsNoTracking()
            .Where(thread => thread.Id == report.TargetId)
            .Select(thread => new
            {
                thread.Title,
                thread.Status,
                thread.CreatedAtUtc,
                thread.AuthorUserId,
                CategoryName = thread.Category!.Name,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new ModerationTarget(
                report.Id,
                report.TargetType.ToString(),
                report.TargetId,
                await HandleAsync(row.AuthorUserId, cancellationToken),
                row.Status.ToString(),
                row.Title,
                $"In {row.CategoryName}.",
                row.CreatedAtUtc);
    }

    /// <summary>
    /// Loads exactly the reported message. The conversation it belongs to is deliberately not
    /// read, so nothing either party said before or after it is exposed.
    /// </summary>
    private async Task<ModerationTarget?> LoadMessageTargetAsync(
        Report report,
        CancellationToken cancellationToken)
    {
        var row = await context.DirectMessages
            .AsNoTracking()
            .Where(message => message.Id == report.TargetId)
            .Select(message => new
            {
                message.Body,
                message.Status,
                message.CreatedAtUtc,
                message.SenderUserId,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new ModerationTarget(
                report.Id,
                report.TargetType.ToString(),
                report.TargetId,
                await HandleAsync(row.SenderUserId, cancellationToken),
                row.Status.ToString(),
                row.Body,
                "A single reported direct message. The rest of the conversation is not shown.",
                row.CreatedAtUtc);
    }

    private async Task<ModerationTarget?> LoadProfileTargetAsync(
        Report report,
        CancellationToken cancellationToken)
    {
        CommunityProfile? profile = await context.CommunityProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.UserId == report.TargetId, cancellationToken);

        return profile is null
            ? null
            : new ModerationTarget(
                report.Id,
                report.TargetType.ToString(),
                report.TargetId,
                profile.Handle,
                profile.Status.ToString(),
                profile.Bio ?? string.Empty,
                "A community profile.",
                profile.CreatedAtUtc);
    }

    private async Task<string> HandleAsync(Guid userId, CancellationToken cancellationToken) =>
        await context.CommunityProfiles
            .AsNoTracking()
            .Where(profile => profile.UserId == userId)
            .Select(profile => profile.Handle)
            .FirstOrDefaultAsync(cancellationToken) ?? "Former member";

    /// <summary>Open and Reviewing are workable; Resolved and Dismissed are final.</summary>
    private static bool CanTransition(ReportStatus current, ReportStatus target) =>
        (current, target) switch
        {
            (ReportStatus.Open, ReportStatus.Reviewing) => true,
            (ReportStatus.Open, ReportStatus.Resolved) => true,
            (ReportStatus.Open, ReportStatus.Dismissed) => true,
            (ReportStatus.Reviewing, ReportStatus.Resolved) => true,
            (ReportStatus.Reviewing, ReportStatus.Dismissed) => true,
            _ => false,
        };

    private static OperationResult? ValidateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "reason",
                "A reason is required and is recorded in the audit trail.");
        }

        return reason.Trim().Length > MaxReasonLength
            ? OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "reason",
                $"Reason must be {MaxReasonLength} characters or fewer.")
            : null;
    }

    /// <summary>
    /// Tells the affected member that a decision was taken. The notification carries no body
    /// or reason text — only a pointer — so the moderator's private note stays internal.
    /// </summary>
    private void NotifyMember(
        Guid recipientUserId,
        Guid moderatorUserId,
        string targetType,
        Guid targetId,
        DateTimeOffset now)
    {
        if (recipientUserId == moderatorUserId)
        {
            return;
        }

        context.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(),
            RecipientUserId = recipientUserId,
            ActorUserId = null,
            Kind = NotificationKind.Moderation,
            TargetType = targetType,
            TargetId = targetId,
            CreatedAtUtc = now,
        });
    }

    private static (int Page, int PageSize) Paging(int page, int pageSize) => (
        page < 1 ? 1 : page,
        pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize));
}
