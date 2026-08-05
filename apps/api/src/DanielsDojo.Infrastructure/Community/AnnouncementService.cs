using DanielsDojo.Application.Common;
using DanielsDojo.Application.Community;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Community;
using DanielsDojo.Infrastructure.Auditing;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DanielsDojo.Infrastructure.Community;

/// <summary>
/// Posts course announcements as pinned threads and fans the pointer out to enrolled members.
/// </summary>
internal sealed class AnnouncementService : IAnnouncementService
{
    /// <summary>Reserved category slug. Created on first use, never duplicated.</summary>
    private const string CategorySlug = "announcements";

    private const int MaxTitleLength = 200;
    private const int MaxBodyLength = 8000;

    private readonly DanielsDojoDbContext context;
    private readonly TimeProvider timeProvider;
    private readonly IRealtimeNotifier realtime;
    private readonly AuditTrail audit;

    public AnnouncementService(
        DanielsDojoDbContext context,
        IOperationContext operationContext,
        TimeProvider timeProvider,
        IRealtimeNotifier realtime)
    {
        this.context = context;
        this.timeProvider = timeProvider;
        this.realtime = realtime;

        audit = new AuditTrail(context, operationContext, timeProvider);
    }

    public async Task<OperationResult<AnnouncementPosted>> PostAsync(
        Guid actorUserId,
        Guid courseId,
        PostAnnouncementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = new ValidationBuilder()
            .Required("title", request.Title, MaxTitleLength, "Title")
            .Required("body", request.Body, MaxBodyLength, "Body");

        if (validation.HasErrors)
        {
            return validation.ToResult().ToFailure<AnnouncementPosted>();
        }

        Course? course = await context.Courses
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == courseId, cancellationToken);

        if (course is null)
        {
            return OperationResult.NotFound().ToFailure<AnnouncementPosted>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        ForumCategory category = await EnsureCategoryAsync(now, cancellationToken);

        var thread = new ForumThread
        {
            Id = Guid.CreateVersion7(),
            CategoryId = category.Id,
            CourseId = courseId,
            AuthorUserId = actorUserId,
            Title = request.Title.Trim(),
            Status = ForumThreadStatus.Open,
            IsPinned = true,
            LastActivityAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.ForumThreads.Add(thread);
        context.ForumPosts.Add(new ForumPost
        {
            Id = Guid.CreateVersion7(),
            ThreadId = thread.Id,
            AuthorUserId = actorUserId,
            Body = request.Body.Trim(),
            Status = ForumPostStatus.Published,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        // Everyone enrolled in the course hears about it — a pointer, never the content.
        List<Guid> enrolled = await context.Enrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.CourseId == courseId
                && enrollment.UserId != actorUserId)
            .Select(enrollment => enrollment.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (Guid recipient in enrolled)
        {
            context.Notifications.Add(new Notification
            {
                Id = Guid.CreateVersion7(),
                RecipientUserId = recipient,
                ActorUserId = actorUserId,
                Kind = NotificationKind.CourseAnnouncement,
                TargetType = "Thread",
                TargetId = thread.Id,
                CreatedAtUtc = now,
            });
        }

        audit.Append(
            "Community.Announcement.Posted",
            nameof(ForumThread),
            thread.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["courseId"] = courseId.ToString("D"),
                ["membersNotified"] = enrolled.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            });

        await context.SaveChangesAsync(cancellationToken);

        // Persisted first, rung after.
        foreach (Guid recipient in enrolled)
        {
            await realtime.UnreadChangedAsync(recipient, cancellationToken);
        }

        return OperationResult.FromValue(new AnnouncementPosted(thread.Id, enrolled.Count));
    }

    /// <summary>Finds or creates the reserved category, tolerant of a concurrent creator.</summary>
    private async Task<ForumCategory> EnsureCategoryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ForumCategory? existing = await context.ForumCategories
            .FirstOrDefaultAsync(category => category.Slug == CategorySlug, cancellationToken);

        if (existing is not null)
        {
            if (existing.Status != ForumCategoryStatus.Active)
            {
                existing.Status = ForumCategoryStatus.Active;
                existing.UpdatedAtUtc = now;
            }

            return existing;
        }

        var created = new ForumCategory
        {
            Id = Guid.CreateVersion7(),
            Slug = CategorySlug,
            Name = "Announcements",
            Description = "Official announcements about courses and the platform.",
            SortOrder = 0,
            Status = ForumCategoryStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.ForumCategories.Add(created);
        return created;
    }
}
