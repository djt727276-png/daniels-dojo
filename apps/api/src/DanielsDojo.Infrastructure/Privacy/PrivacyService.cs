using DanielsDojo.Application.Common;
using DanielsDojo.Application.Privacy;
using DanielsDojo.Domain.Community;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Domain.Learning;
using DanielsDojo.Infrastructure.Auditing;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DanielsDojo.Infrastructure.Privacy;

/// <summary>
/// Export and deletion, implemented exactly as the retention policy documents them.
/// </summary>
/// <remarks>
/// <para>
/// Export reads only rows the member owns. Messages they received, other members' posts,
/// and moderation records about them are other people's data or the operator's and are
/// deliberately absent.
/// </para>
/// <para>
/// Deletion runs in one transaction. Community presence — profile, avatar, relationships,
/// read states, inbox — is removed outright. Sent messages and reviews become tombstones.
/// Forum posts survive unattributed ("Former member"), because removing them would tear
/// holes in other members' conversations. Orders, subscriptions, entitlements, enrollments,
/// certificates, and audit rows are retained: financial and issuance records the platform
/// is required to keep, now tied to a scrubbed, sign-in-proof account row. The external
/// subject binding is overwritten, so the same person signing in again starts from nothing.
/// </para>
/// </remarks>
internal sealed class PrivacyService : IPrivacyService
{
    private readonly DanielsDojoDbContext context;
    private readonly AuditTrail auditTrail;
    private readonly TimeProvider timeProvider;

    public PrivacyService(
        DanielsDojoDbContext context,
        IOperationContext operationContext,
        TimeProvider timeProvider)
    {
        this.context = context;
        this.timeProvider = timeProvider;
        auditTrail = new AuditTrail(context, operationContext, timeProvider);
    }

    public async Task<PersonalDataExport> ExportAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        User account = await context.Users
            .AsNoTracking()
            .SingleAsync(user => user.Id == userId, cancellationToken);

        var profile = await context.CommunityProfiles
            .AsNoTracking()
            .Where(candidate => candidate.UserId == userId)
            .Select(candidate => new
            {
                candidate.Handle,
                candidate.Bio,
                candidate.IsDiscoverable,
                candidate.FriendRequestPolicy,
                candidate.MessagePolicy,
                candidate.CreatedAtUtc,
            })
            .FirstOrDefaultAsync(cancellationToken);

        bool hasAvatar = await context.ProfileAvatars
            .AnyAsync(avatar => avatar.UserId == userId, cancellationToken);

        List<ExportedFriend> friends = await context.Friendships
            .AsNoTracking()
            .Where(friendship => friendship.UserLowId == userId || friendship.UserHighId == userId)
            .Select(friendship => new ExportedFriend(
                context.CommunityProfiles
                    .Where(other => other.UserId
                        == (friendship.UserLowId == userId ? friendship.UserHighId : friendship.UserLowId))
                    .Select(other => other.Handle)
                    .FirstOrDefault() ?? "Former member",
                friendship.AcceptedAtUtc))
            .ToListAsync(cancellationToken);

        List<ExportedMessage> messages = await context.DirectMessages
            .AsNoTracking()
            .Where(message => message.SenderUserId == userId)
            .OrderBy(message => message.CreatedAtUtc)
            .Select(message => new ExportedMessage(
                context.CommunityProfiles
                    .Where(other => other.UserId
                        == (message.Conversation!.UserLowId == userId
                            ? message.Conversation.UserHighId
                            : message.Conversation.UserLowId))
                    .Select(other => other.Handle)
                    .FirstOrDefault() ?? "Former member",
                message.Body,
                message.Status.ToString(),
                message.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        List<ExportedForumPost> posts = await context.ForumPosts
            .AsNoTracking()
            .Where(post => post.AuthorUserId == userId)
            .OrderBy(post => post.CreatedAtUtc)
            .Select(post => new ExportedForumPost(
                post.Thread!.Title,
                post.Body,
                post.Status.ToString(),
                post.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        List<ExportedReview> reviews = await context.CourseReviews
            .AsNoTracking()
            .Where(review => review.UserId == userId)
            .Select(review => new ExportedReview(
                review.Course!.Title,
                review.Rating,
                review.Body,
                review.Status.ToString(),
                review.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        List<ExportedEnrollment> enrollments = await context.Enrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.UserId == userId)
            .Select(enrollment => new ExportedEnrollment(
                enrollment.Course!.Title,
                enrollment.EnrolledAtUtc,
                context.LessonProgress.Count(progress =>
                    progress.UserId == userId
                    && progress.CompletedAtUtc != null
                    && progress.Lesson!.CourseId == enrollment.CourseId)))
            .ToListAsync(cancellationToken);

        List<ExportedCertificate> certificates = await context.Certificates
            .AsNoTracking()
            .Where(certificate => certificate.UserId == userId)
            .Select(certificate => new ExportedCertificate(
                certificate.CourseTitleAtIssue,
                certificate.VerificationCode,
                certificate.RevokedAtUtc == null ? "Issued" : "Revoked",
                certificate.IssuedAtUtc))
            .ToListAsync(cancellationToken);

        List<ExportedOrder> orders = await context.Orders
            .AsNoTracking()
            .Where(order => order.UserId == userId)
            .Select(order => new ExportedOrder(
                order.Id,
                order.Status.ToString(),
                order.TotalMinor,
                order.Currency,
                order.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        auditTrail.Append("privacy.export", "User", userId);
        await context.SaveChangesAsync(cancellationToken);

        return new PersonalDataExport(
            timeProvider.GetUtcNow(),
            new ExportedAccount(
                account.DisplayName, account.Email, account.EmailVerified, account.CreatedAtUtc),
            profile is null
                ? null
                : new ExportedCommunityProfile(
                    profile.Handle,
                    profile.Bio,
                    hasAvatar,
                    profile.IsDiscoverable,
                    profile.FriendRequestPolicy.ToString(),
                    profile.MessagePolicy.ToString(),
                    profile.CreatedAtUtc),
            friends,
            messages,
            posts,
            reviews,
            enrollments,
            certificates,
            orders);
    }

    public async Task<OperationResult> DeleteAccountAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        User? account = await context.Users
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

        if (account is null || account.Status != UserStatus.Active)
        {
            return OperationResult.NotFound();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        // Community presence: gone outright.
        await context.ProfileAvatars
            .Where(avatar => avatar.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.ForumSubscriptions
            .Where(subscription => subscription.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.ForumPostReactions
            .Where(reaction => reaction.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.FriendRequests
            .Where(request => request.UserLowId == userId || request.UserHighId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.Friendships
            .Where(friendship => friendship.UserLowId == userId || friendship.UserHighId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.UserBlocks
            .Where(block => block.BlockerUserId == userId || block.BlockedUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.ConversationReadStates
            .Where(state => state.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.Notifications
            .Where(notification => notification.RecipientUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.CommunityProfiles
            .Where(profile => profile.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        // Sent messages become tombstones: the other member keeps the shape of the
        // conversation, the departing member's words are gone.
        await context.DirectMessages
            .Where(message => message.SenderUserId == userId
                && message.Status != DirectMessageStatus.Deleted)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(message => message.Body, string.Empty)
                    .SetProperty(message => message.Status, DirectMessageStatus.Deleted)
                    .SetProperty(message => message.DeletedAtUtc, now)
                    .SetProperty(message => message.UpdatedAtUtc, now),
                cancellationToken);

        // Reviews carry the member's name on a public page; tombstone them.
        await context.CourseReviews
            .Where(review => review.UserId == userId
                && review.Status != CourseReviewStatus.Deleted)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(review => review.Status, CourseReviewStatus.Deleted)
                    .SetProperty(review => review.UpdatedAtUtc, now),
                cancellationToken);

        // Roles go: a deleted administrator must not remain an administrator on paper.
        await context.UserRoles
            .Where(role => role.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        // The account row is scrubbed and its sign-in binding overwritten. The overwrite is
        // what makes deletion final: the identity provider's subject no longer maps to this
        // row, so the same person registering again starts a brand-new account.
        account.DisplayName = "Deleted member";
        account.Email = string.Empty;
        account.NormalizedEmail = string.Empty;
        account.EmailVerified = false;
        account.ExternalSubjectId = $"deleted:{userId:D}";
        account.Status = UserStatus.Disabled;
        account.UpdatedAtUtc = now;

        auditTrail.Append(
            "privacy.account_deleted",
            "User",
            userId,
            reason: "Deletion requested by the account holder.");

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return OperationResult.Success();
    }
}
