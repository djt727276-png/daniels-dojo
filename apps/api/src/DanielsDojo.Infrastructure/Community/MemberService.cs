using DanielsDojo.Application.Common;
using DanielsDojo.Application.Community;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Community;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DanielsDojo.Infrastructure.Community;

/// <summary>
/// The signed-in member's own dashboard, learning list, and community profile.
/// </summary>
/// <remarks>
/// Every count here is a real query. Purchasing and enrollment belong to a later phase, so the
/// enrolled count is legitimately zero today; the dashboard says so plainly rather than showing
/// invented progress.
/// </remarks>
internal sealed class MemberService : IMemberService
{
    /// <summary>Hard ceiling on the enrolled-course list, which has no paging of its own.</summary>
    private const int MaxCourseListSize = 200;

    private readonly DanielsDojoDbContext context;
    private readonly ICommunityAccessEvaluator accessEvaluator;
    private readonly TimeProvider timeProvider;

    public MemberService(
        DanielsDojoDbContext context,
        ICommunityAccessEvaluator accessEvaluator,
        TimeProvider timeProvider)
    {
        this.context = context;
        this.accessEvaluator = accessEvaluator;
        this.timeProvider = timeProvider;
    }

    public async Task<DashboardResponse> GetDashboardAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var identity = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.DisplayName,
                Roles = user.UserRoles
                    .Select(link => link.Role!.Name)
                    .OrderBy(name => name)
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        CommunityAccess access = await accessEvaluator.EvaluateAsync(userId, cancellationToken);

        int enrolled = await context.Enrollments
            .CountAsync(enrollment => enrollment.UserId == userId, cancellationToken);

        int published = await context.Courses
            .CountAsync(course => course.Status == PublicationStatus.Published, cancellationToken);

        int notifications = await context.Notifications
            .CountAsync(
                notification => notification.RecipientUserId == userId && notification.ReadAtUtc == null,
                cancellationToken);

        // A request is "mine to answer" when I am in the pair but did not send it.
        int friendRequests = await context.FriendRequests
            .CountAsync(
                request => (request.UserLowId == userId || request.UserHighId == userId)
                    && request.RequestedByUserId != userId
                    && request.Status == FriendRequestStatus.Pending,
                cancellationToken);

        int unreadConversations = await CountUnreadConversationsAsync(userId, cancellationToken);

        return new DashboardResponse(
            identity?.DisplayName ?? string.Empty,
            identity?.Roles ?? [],
            enrolled,
            published,
            notifications,
            friendRequests,
            unreadConversations,
            CommunityStatusResponse.From(access),

            // Checkout arrives in a later phase. Saying so is more useful than a button that
            // cannot complete.
            PurchasingAvailable: false);
    }

    public async Task<IReadOnlyList<MyCourse>> GetMyCoursesAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await context.Enrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.UserId == userId)
            .OrderByDescending(enrollment => enrollment.LastAccessedAtUtc ?? enrollment.EnrolledAtUtc)
            .ThenBy(enrollment => enrollment.Id)
            .Take(MaxCourseListSize)
            .Select(enrollment => new MyCourse(
                enrollment.Course!.Slug,
                enrollment.Course.Title,
                enrollment.Course.Summary,
                enrollment.Course.Level.ToString(),
                enrollment.EnrolledAtUtc,
                enrollment.LastAccessedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<MyCommunityProfile?> GetCommunityProfileAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        CommunityProfile? profile = await context.CommunityProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

        return profile is null
            ? null
            : Project(profile, await HasAvatarAsync(userId, cancellationToken));
    }

    public async Task<OperationResult<MyCommunityProfile>> CompleteCommunitySetupAsync(
        Guid userId,
        CompleteCommunitySetupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await context.CommunityProfiles.AnyAsync(
                profile => profile.UserId == userId, cancellationToken))
        {
            return OperationResult.Conflict(
                ErrorCodes.DuplicateValue,
                "Your community profile already exists.").ToFailure<MyCommunityProfile>();
        }

        var validation = new ValidationBuilder()
            .When(!CommunityHandle.IsValid(request.Handle?.Trim()), "handle", CommunityHandle.Requirement)
            .Optional("bio", request.Bio, 500, "Bio")
            .When(
                !request.AcceptGuidelines,
                "acceptGuidelines",
                "Accept the community guidelines to continue.")
            .When(
                !request.AttestEligibility,
                "attestEligibility",
                "Confirm you meet the age policy to continue.");

        if (validation.HasErrors)
        {
            return validation.ToResult().ToFailure<MyCommunityProfile>();
        }

        // Validation above rejects a null or blank handle, so this is non-null by construction.
        string handle = request.Handle!.Trim();
        string normalized = CommunityHandle.Normalize(handle);

        if (await context.CommunityProfiles.AnyAsync(
                profile => profile.NormalizedHandle == normalized, cancellationToken))
        {
            return OperationResult.Invalid(
                ErrorCodes.DuplicateValue,
                "handle",
                "That handle is taken.").ToFailure<MyCommunityProfile>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var created = new CommunityProfile
        {
            UserId = userId,
            Handle = handle,
            NormalizedHandle = normalized,
            Bio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim(),

            // Everything starts closed. Opening up is a deliberate later choice, not the
            // consequence of finishing setup.
            IsDiscoverable = false,
            FriendRequestPolicy = FriendRequestPolicy.NoOne,
            MessagePolicy = MessagePolicy.NoOne,
            Status = CommunityProfileStatus.Active,
            GuidelinesVersion = CommunityGuidelines.CurrentVersion,
            GuidelinesAcceptedAtUtc = now,
            EligibilityAttestedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.CommunityProfiles.Add(created);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent setup won the unique handle index.
            context.ChangeTracker.Clear();
            return OperationResult.Invalid(
                ErrorCodes.DuplicateValue,
                "handle",
                "That handle is taken.").ToFailure<MyCommunityProfile>();
        }

        // A brand-new profile cannot have an avatar yet: uploads require the profile.
        return OperationResult.FromValue(Project(created, hasAvatar: false));
    }

    public async Task<OperationResult<MyCommunityProfile>> UpdateCommunityProfileAsync(
        Guid userId,
        UpdateCommunityProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CommunityProfile? profile = await context.CommunityProfiles
            .FirstOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

        if (profile is null)
        {
            return OperationResult.NotFound().ToFailure<MyCommunityProfile>();
        }

        var validation = new ValidationBuilder()
            .Optional("bio", request.Bio, 500, "Bio")
            .When(
                !Enum.TryParse(request.FriendRequestPolicy, ignoreCase: true, out FriendRequestPolicy _),
                "friendRequestPolicy",
                "Choose a valid friend request setting.")
            .When(
                !Enum.TryParse(request.MessagePolicy, ignoreCase: true, out MessagePolicy _),
                "messagePolicy",
                "Choose a valid message setting.");

        if (validation.HasErrors)
        {
            return validation.ToResult().ToFailure<MyCommunityProfile>();
        }

        if (!RowVersionToken.TryDecode(request.RowVersion, out byte[] rowVersion))
        {
            return OperationResult.Invalid(
                ErrorCodes.InvalidRowVersion,
                "rowVersion",
                "The supplied version token is not valid. Reload and try again.")
                .ToFailure<MyCommunityProfile>();
        }

        context.Entry(profile).Property(nameof(CommunityProfile.RowVersion)).OriginalValue = rowVersion;

        profile.Bio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim();
        profile.IsDiscoverable = request.IsDiscoverable;
        profile.FriendRequestPolicy =
            Enum.Parse<FriendRequestPolicy>(request.FriendRequestPolicy, ignoreCase: true);
        profile.MessagePolicy = Enum.Parse<MessagePolicy>(request.MessagePolicy, ignoreCase: true);
        profile.UpdatedAtUtc = timeProvider.GetUtcNow();

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return OperationResult.ConcurrencyConflict().ToFailure<MyCommunityProfile>();
        }

        return OperationResult.FromValue(
            Project(profile, await HasAvatarAsync(userId, cancellationToken)));
    }

    /// <summary>Counts conversations holding a message the member has not read yet.</summary>
    private Task<int> CountUnreadConversationsAsync(Guid userId, CancellationToken cancellationToken) =>
        context.DirectConversations
            .AsNoTracking()
            .Where(conversation => conversation.UserLowId == userId || conversation.UserHighId == userId)
            .CountAsync(
                conversation => context.DirectMessages.Any(message =>
                    message.ConversationId == conversation.Id
                    && message.SenderUserId != userId
                    && message.Status != DirectMessageStatus.Deleted
                    && !context.ConversationReadStates.Any(state =>
                        state.ConversationId == conversation.Id
                        && state.UserId == userId
                        && state.LastReadAtUtc >= message.CreatedAtUtc)),
                cancellationToken);

    private Task<bool> HasAvatarAsync(Guid userId, CancellationToken cancellationToken) =>
        context.ProfileAvatars.AnyAsync(avatar => avatar.UserId == userId, cancellationToken);

    private static MyCommunityProfile Project(CommunityProfile profile, bool hasAvatar) => new(
        profile.Handle,
        profile.Bio,
        hasAvatar,
        profile.IsDiscoverable,
        profile.FriendRequestPolicy.ToString(),
        profile.MessagePolicy.ToString(),
        profile.Status.ToString(),
        profile.GuidelinesVersion,
        profile.GuidelinesAcceptedAtUtc,
        profile.EligibilityAttestedAtUtc is not null,
        profile.IsParticipationReady,
        RowVersionToken.Encode(profile.RowVersion));
}
