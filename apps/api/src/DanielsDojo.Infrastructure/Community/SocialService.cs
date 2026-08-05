using DanielsDojo.Application.Catalog;
using DanielsDojo.Application.Common;
using DanielsDojo.Application.Community;
using DanielsDojo.Domain.Community;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DanielsDojo.Infrastructure.Community;

/// <summary>
/// Friendships, blocks, direct messages, and the notification inbox.
/// </summary>
/// <remarks>
/// <para>
/// Three invariants run through everything here. A block is symmetric — once either side has
/// blocked the other, neither can contact, discover, or be notified about the other. Contact is
/// opt-in — a member with the default settings receives no requests and no messages at all.
/// And unordered pairs are stored canonically, so one row can only ever mean one relationship.
/// </para>
/// </remarks>
internal sealed class SocialService : ISocialService
{
    private const int MaxMessageLength = 4000;
    private const int DefaultPageSize = 30;
    private const int MaxPageSize = 100;
    private const int MaxSearchResults = 20;

    /// <summary>
    /// Hard ceiling on the un-paged personal lists: friends, requests, blocks, conversations.
    /// These grow with a member's own activity, so an unbounded projection is a response size
    /// nobody controls. Ordering is deterministic, so the cap always keeps the same rows.
    /// </summary>
    private const int MaxPersonalListSize = 200;

    private readonly DanielsDojoDbContext context;
    private readonly ICommunityAccessEvaluator accessEvaluator;
    private readonly TimeProvider timeProvider;
    private readonly IRealtimeNotifier realtime;

    public SocialService(
        DanielsDojoDbContext context,
        ICommunityAccessEvaluator accessEvaluator,
        TimeProvider timeProvider,
        IRealtimeNotifier realtime)
    {
        this.context = context;
        this.accessEvaluator = accessEvaluator;
        this.timeProvider = timeProvider;
        this.realtime = realtime;
    }

    // ------------------------------------------------------------------ discovery

    public async Task<OperationResult<IReadOnlyList<MemberCard>>> SearchMembersAsync(
        Guid viewerUserId,
        string? search,
        CancellationToken cancellationToken = default)
    {
        OperationResult? denied = await RequireParticipationAsync(viewerUserId, cancellationToken);

        if (denied is not null)
        {
            return denied.ToFailure<IReadOnlyList<MemberCard>>();
        }

        string term = (search ?? string.Empty).Trim().ToUpperInvariant();

        if (term.Length < 2)
        {
            return OperationResult.FromValue<IReadOnlyList<MemberCard>>([]);
        }

        HashSet<Guid> blocked = await BlockedEitherWayAsync(viewerUserId, cancellationToken);

        // Only members who opted into discovery appear. There is no way to enumerate the rest.
        List<CommunityProfile> matches = await context.CommunityProfiles
            .AsNoTracking()
            .Where(profile => profile.IsDiscoverable
                && profile.Status == CommunityProfileStatus.Active
                && profile.UserId != viewerUserId
                && profile.NormalizedHandle.StartsWith(term))
            .OrderBy(profile => profile.NormalizedHandle)
            .Take(MaxSearchResults)
            .ToListAsync(cancellationToken);

        List<MemberCard> cards = [];

        foreach (CommunityProfile profile in matches.Where(profile => !blocked.Contains(profile.UserId)))
        {
            cards.Add(await ToCardAsync(viewerUserId, profile, cancellationToken));
        }

        return OperationResult.FromValue<IReadOnlyList<MemberCard>>(cards);
    }

    public async Task<OperationResult<MemberCard>> GetMemberAsync(
        Guid viewerUserId,
        string handle,
        CancellationToken cancellationToken = default)
    {
        OperationResult? denied = await RequireParticipationAsync(viewerUserId, cancellationToken);

        if (denied is not null)
        {
            return denied.ToFailure<MemberCard>();
        }

        string normalized = CommunityHandle.Normalize(handle ?? string.Empty);

        CommunityProfile? profile = await context.CommunityProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.NormalizedHandle == normalized
                    && candidate.Status == CommunityProfileStatus.Active,
                cancellationToken);

        if (profile is null || profile.UserId == viewerUserId)
        {
            return OperationResult.NotFound().ToFailure<MemberCard>();
        }

        // Blocked and undiscoverable members are reported as missing, so neither the block nor
        // the privacy setting is observable from outside.
        HashSet<Guid> blocked = await BlockedEitherWayAsync(viewerUserId, cancellationToken);
        bool isFriend = await AreFriendsAsync(viewerUserId, profile.UserId, cancellationToken);

        if (blocked.Contains(profile.UserId) || (!profile.IsDiscoverable && !isFriend))
        {
            return OperationResult.NotFound().ToFailure<MemberCard>();
        }

        return OperationResult.FromValue(await ToCardAsync(viewerUserId, profile, cancellationToken));
    }

    // ------------------------------------------------------------------ friendships

    public async Task<IReadOnlyList<FriendView>> ListFriendsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Ordering and paging a joined projection is not something the provider can translate,
        // so the pair rows are fetched first and the handles attached afterwards. The cap is
        // applied in SQL, where it actually limits what the database returns.
        var pairs = await context.Friendships
            .AsNoTracking()
            .Where(friendship => friendship.UserLowId == userId || friendship.UserHighId == userId)
            .OrderBy(friendship => friendship.AcceptedAtUtc)
            .ThenBy(friendship => friendship.Id)
            .Take(MaxPersonalListSize)
            .Select(friendship => new
            {
                OtherId = friendship.UserLowId == userId ? friendship.UserHighId : friendship.UserLowId,
                friendship.AcceptedAtUtc,
            })
            .ToListAsync(cancellationToken);

        Dictionary<Guid, string> handles = await HandlesAsync(
            pairs.Select(pair => pair.OtherId), cancellationToken);

        return pairs
            .Select(pair => new FriendView(
                pair.OtherId,
                handles.GetValueOrDefault(pair.OtherId, "Former member"),
                pair.AcceptedAtUtc))
            .OrderBy(friend => friend.Handle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(friend => friend.UserId)
            .ToList();
    }

    public async Task<IReadOnlyList<FriendRequestView>> ListFriendRequestsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // The row version is encoded after materialisation: Base64 has no SQL translation, so
        // projecting it inside the query would fail at run time rather than at compile time.
        var rows = await context.FriendRequests
            .AsNoTracking()
            .Where(request => (request.UserLowId == userId || request.UserHighId == userId)
                && request.Status == FriendRequestStatus.Pending)
            .Select(request => new
            {
                request.Id,
                OtherId = request.UserLowId == userId ? request.UserHighId : request.UserLowId,
                Incoming = request.RequestedByUserId != userId,
                request.RequestedAtUtc,
                request.RowVersion,
            })
            .OrderByDescending(request => request.RequestedAtUtc)
            .ThenBy(request => request.Id)
            .Take(MaxPersonalListSize)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, string> handles = await HandlesAsync(
            rows.Select(row => row.OtherId), cancellationToken);

        return rows
            .Select(row => new FriendRequestView(
                row.Id,
                row.OtherId,
                handles.GetValueOrDefault(row.OtherId, "Former member"),
                row.Incoming,
                row.RequestedAtUtc,
                RowVersionToken.Encode(row.RowVersion)))
            .ToList();
    }

    public async Task<OperationResult> SendFriendRequestAsync(
        Guid senderUserId,
        SendFriendRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult? denied = await RequireParticipationAsync(senderUserId, cancellationToken);

        if (denied is not null)
        {
            return denied;
        }

        CommunityProfile? target = await FindActiveProfileAsync(request.Handle, cancellationToken);

        if (target is null || target.UserId == senderUserId)
        {
            return OperationResult.NotFound();
        }

        if (await IsBlockedEitherWayAsync(senderUserId, target.UserId, cancellationToken))
        {
            // Deliberately the same refusal a closed setting produces, so a block is not
            // distinguishable from "not accepting requests".
            return OperationResult.Forbidden(
                ErrorCodes.CommunityBlocked,
                "You cannot send this member a friend request.");
        }

        if (target.FriendRequestPolicy != FriendRequestPolicy.Everyone)
        {
            return OperationResult.Forbidden(
                ErrorCodes.CommunityForbidden,
                "This member is not accepting friend requests.");
        }

        (Guid low, Guid high) = CanonicalPair.Order(senderUserId, target.UserId);

        if (await context.Friendships.AnyAsync(
                friendship => friendship.UserLowId == low && friendship.UserHighId == high,
                cancellationToken))
        {
            return OperationResult.Conflict(ErrorCodes.DuplicateValue, "You are already friends.");
        }

        if (await context.FriendRequests.AnyAsync(
                pending => pending.UserLowId == low
                    && pending.UserHighId == high
                    && pending.Status == FriendRequestStatus.Pending,
                cancellationToken))
        {
            return OperationResult.Conflict(
                ErrorCodes.DuplicateValue,
                "There is already a request waiting for an answer.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var friendRequest = new FriendRequest
        {
            Id = Guid.CreateVersion7(),
            UserLowId = low,
            UserHighId = high,
            RequestedByUserId = senderUserId,
            Status = FriendRequestStatus.Pending,
            RequestedAtUtc = now,
        };

        context.FriendRequests.Add(friendRequest);
        AddNotification(target.UserId, senderUserId, NotificationKind.FriendRequest, "FriendRequest", friendRequest.Id, now);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            return OperationResult.Conflict(
                ErrorCodes.DuplicateValue,
                "There is already a request waiting for an answer.");
        }

        return OperationResult.Success();
    }

    public async Task<OperationResult> RespondToFriendRequestAsync(
        Guid userId,
        Guid requestId,
        string action,
        CancellationToken cancellationToken = default)
    {
        FriendRequest? request = await context.FriendRequests
            .FirstOrDefaultAsync(candidate => candidate.Id == requestId, cancellationToken);

        if (request is null
            || (request.UserLowId != userId && request.UserHighId != userId)
            || request.Status != FriendRequestStatus.Pending)
        {
            return OperationResult.NotFound();
        }

        bool isRecipient = request.RequestedByUserId != userId;
        DateTimeOffset now = timeProvider.GetUtcNow();

        switch (action.ToUpperInvariant())
        {
            case "ACCEPT" when isRecipient:
                request.Status = FriendRequestStatus.Accepted;
                request.RespondedAtUtc = now;

                context.Friendships.Add(new Friendship
                {
                    Id = Guid.CreateVersion7(),
                    UserLowId = request.UserLowId,
                    UserHighId = request.UserHighId,
                    AcceptedAtUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                });

                AddNotification(
                    request.RequestedByUserId,
                    userId,
                    NotificationKind.FriendAccepted,
                    "Friendship",
                    request.Id,
                    now);
                break;

            case "DECLINE" when isRecipient:
                request.Status = FriendRequestStatus.Declined;
                request.RespondedAtUtc = now;
                break;

            case "CANCEL" when !isRecipient:
                request.Status = FriendRequestStatus.Cancelled;
                request.RespondedAtUtc = now;
                break;

            default:
                // Accepting your own request, or cancelling someone else's, is simply not a
                // thing that exists.
                return OperationResult.NotFound();
        }

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    public async Task<OperationResult> RemoveFriendAsync(
        Guid userId,
        Guid otherUserId,
        CancellationToken cancellationToken = default)
    {
        (Guid low, Guid high) = CanonicalPair.Order(userId, otherUserId);

        Friendship? friendship = await context.Friendships.FirstOrDefaultAsync(
            candidate => candidate.UserLowId == low && candidate.UserHighId == high,
            cancellationToken);

        if (friendship is null)
        {
            return OperationResult.NotFound();
        }

        context.Friendships.Remove(friendship);
        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    // ------------------------------------------------------------------ blocks

    public async Task<IReadOnlyList<BlockView>> ListBlocksAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var blocks = await context.UserBlocks
            .AsNoTracking()
            .Where(block => block.BlockerUserId == userId)
            .OrderBy(block => block.CreatedAtUtc)
            .ThenBy(block => block.BlockedUserId)
            .Take(MaxPersonalListSize)
            .Select(block => new
            {
                block.BlockedUserId,
                block.ReasonCategory,
                block.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        Dictionary<Guid, string> handles = await HandlesAsync(
            blocks.Select(block => block.BlockedUserId), cancellationToken);

        return blocks
            .Select(block => new BlockView(
                block.BlockedUserId,
                handles.GetValueOrDefault(block.BlockedUserId, "Former member"),
                block.ReasonCategory.ToString(),
                block.CreatedAtUtc))
            .OrderBy(block => block.Handle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(block => block.UserId)
            .ToList();
    }

    public async Task<OperationResult> BlockAsync(
        Guid userId,
        CreateBlockRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult? denied = await RequireParticipationAsync(userId, cancellationToken);

        if (denied is not null)
        {
            return denied;
        }

        CommunityProfile? target = await FindActiveProfileAsync(request.Handle, cancellationToken);

        if (target is null || target.UserId == userId)
        {
            return OperationResult.NotFound();
        }

        if (!Enum.TryParse(request.ReasonCategory ?? nameof(BlockReasonCategory.Unspecified),
                ignoreCase: true,
                out BlockReasonCategory reason))
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed, "reasonCategory", "Choose a valid reason.");
        }

        if (await context.UserBlocks.AnyAsync(
                block => block.BlockerUserId == userId && block.BlockedUserId == target.UserId,
                cancellationToken))
        {
            return OperationResult.Success();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        context.UserBlocks.Add(new UserBlock
        {
            BlockerUserId = userId,
            BlockedUserId = target.UserId,
            ReasonCategory = reason,
            CreatedAtUtc = now,
        });

        // Blocking withdraws the relationship as well as future contact. Leaving a friendship
        // in place would keep the pair inside every "friends only" allowance.
        (Guid low, Guid high) = CanonicalPair.Order(userId, target.UserId);

        Friendship? friendship = await context.Friendships.FirstOrDefaultAsync(
            candidate => candidate.UserLowId == low && candidate.UserHighId == high, cancellationToken);

        if (friendship is not null)
        {
            context.Friendships.Remove(friendship);
        }

        List<FriendRequest> pending = await context.FriendRequests
            .Where(candidate => candidate.UserLowId == low
                && candidate.UserHighId == high
                && candidate.Status == FriendRequestStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (FriendRequest item in pending)
        {
            item.Status = FriendRequestStatus.Cancelled;
            item.RespondedAtUtc = now;
        }

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    public async Task<OperationResult> UnblockAsync(
        Guid userId,
        Guid blockedUserId,
        CancellationToken cancellationToken = default)
    {
        UserBlock? block = await context.UserBlocks.FirstOrDefaultAsync(
            candidate => candidate.BlockerUserId == userId && candidate.BlockedUserId == blockedUserId,
            cancellationToken);

        if (block is null)
        {
            return OperationResult.NotFound();
        }

        context.UserBlocks.Remove(block);
        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    // ------------------------------------------------------------------ messages

    public async Task<IReadOnlyList<ConversationSummary>> ListConversationsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        HashSet<Guid> blocked = await BlockedEitherWayAsync(userId, cancellationToken);

        var rows = await context.DirectConversations
            .AsNoTracking()
            .Where(conversation => conversation.UserLowId == userId || conversation.UserHighId == userId)
            .Select(conversation => new
            {
                conversation.Id,
                OtherId = conversation.UserLowId == userId
                    ? conversation.UserHighId
                    : conversation.UserLowId,
                conversation.LastMessageAtUtc,
                UnreadCount = context.DirectMessages.Count(message =>
                    message.ConversationId == conversation.Id
                    && message.SenderUserId != userId
                    && message.Status != DirectMessageStatus.Deleted
                    && !context.ConversationReadStates.Any(state =>
                        state.ConversationId == conversation.Id
                        && state.UserId == userId
                        && state.LastReadAtUtc >= message.CreatedAtUtc)),
            })
            .OrderByDescending(row => row.LastMessageAtUtc)
            .ThenBy(row => row.Id)
            .Take(MaxPersonalListSize)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, string> handles = await HandlesAsync(
            rows.Select(row => row.OtherId), cancellationToken);

        return rows
            .Select(row =>
            {
                bool hidden = blocked.Contains(row.OtherId);

                return new ConversationSummary(
                    row.Id,
                    row.OtherId,
                    hidden ? "Hidden member" : handles.GetValueOrDefault(row.OtherId, "Former member"),
                    hidden,
                    row.LastMessageAtUtc,
                    row.UnreadCount);
            })
            .ToList();
    }

    public async Task<OperationResult<ConversationDetail>> StartConversationAsync(
        Guid userId,
        StartConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult? denied = await RequireParticipationAsync(userId, cancellationToken);

        if (denied is not null)
        {
            return denied.ToFailure<ConversationDetail>();
        }

        CommunityProfile? target = await FindActiveProfileAsync(request.Handle, cancellationToken);

        if (target is null || target.UserId == userId)
        {
            return OperationResult.NotFound().ToFailure<ConversationDetail>();
        }

        OperationResult? refusal = await CheckCanMessageAsync(userId, target, cancellationToken);

        if (refusal is not null)
        {
            return refusal.ToFailure<ConversationDetail>();
        }

        (Guid low, Guid high) = CanonicalPair.Order(userId, target.UserId);

        DirectConversation? conversation = await context.DirectConversations.FirstOrDefaultAsync(
            candidate => candidate.UserLowId == low && candidate.UserHighId == high, cancellationToken);

        if (conversation is null)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            conversation = new DirectConversation
            {
                Id = Guid.CreateVersion7(),
                UserLowId = low,
                UserHighId = high,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            context.DirectConversations.Add(conversation);
            await context.SaveChangesAsync(cancellationToken);
        }

        return await BuildConversationAsync(userId, conversation.Id, 1, DefaultPageSize, cancellationToken);
    }

    public Task<OperationResult<ConversationDetail>> GetConversationAsync(
        Guid userId,
        Guid conversationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        BuildConversationAsync(userId, conversationId, page, pageSize, cancellationToken, markRead: true);

    public async Task<OperationResult<ConversationDetail>> SendMessageAsync(
        Guid userId,
        Guid conversationId,
        SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult? denied = await RequireParticipationAsync(userId, cancellationToken);

        if (denied is not null)
        {
            return denied.ToFailure<ConversationDetail>();
        }

        DirectConversation? conversation = await context.DirectConversations
            .FirstOrDefaultAsync(candidate => candidate.Id == conversationId, cancellationToken);

        if (conversation is null || !conversation.Includes(userId))
        {
            return OperationResult.NotFound().ToFailure<ConversationDetail>();
        }

        var validation = new ValidationBuilder().Required("body", request.Body, MaxMessageLength, "Message");

        if (validation.HasErrors)
        {
            return validation.ToResult().ToFailure<ConversationDetail>();
        }

        Guid otherUserId = conversation.UserLowId == userId
            ? conversation.UserHighId
            : conversation.UserLowId;

        CommunityProfile? other = await context.CommunityProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(profile => profile.UserId == otherUserId, cancellationToken);

        if (other is null)
        {
            return OperationResult.NotFound().ToFailure<ConversationDetail>();
        }

        // Permission is re-checked on every send, so a block or a settings change takes effect
        // immediately rather than only at the moment a conversation was opened.
        OperationResult? refusal = await CheckCanMessageAsync(userId, other, cancellationToken);

        if (refusal is not null)
        {
            return refusal.ToFailure<ConversationDetail>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var message = new DirectMessage
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversationId,
            SenderUserId = userId,
            Body = request.Body.Trim(),
            Status = DirectMessageStatus.Sent,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.DirectMessages.Add(message);
        conversation.LastMessageAtUtc = now;
        conversation.UpdatedAtUtc = now;

        AddNotification(otherUserId, userId, NotificationKind.DirectMessage, "Conversation", conversationId, now);

        await context.SaveChangesAsync(cancellationToken);

        // The message is durable; now the doorbell. A push failure is not a send failure —
        // the recipient reconciles over REST either way.
        await realtime.MessageReceivedAsync(otherUserId, conversationId, cancellationToken);
        await realtime.UnreadChangedAsync(otherUserId, cancellationToken);

        return await BuildConversationAsync(userId, conversationId, 1, DefaultPageSize, cancellationToken);
    }

    public async Task<OperationResult<ConversationDetail>> DeleteMessageAsync(
        Guid userId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        DirectMessage? message = await context.DirectMessages
            .FirstOrDefaultAsync(candidate => candidate.Id == messageId, cancellationToken);

        if (message is null || message.SenderUserId != userId)
        {
            return OperationResult.NotFound().ToFailure<ConversationDetail>();
        }

        if (message.Status != DirectMessageStatus.Deleted)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();

            // Tombstone: the row stays so the conversation still reads in order, but the text
            // is gone from the database rather than merely hidden from one client.
            message.Body = string.Empty;
            message.Status = DirectMessageStatus.Deleted;
            message.DeletedAtUtc = now;
            message.UpdatedAtUtc = now;

            await context.SaveChangesAsync(cancellationToken);
        }

        return await BuildConversationAsync(
            userId, message.ConversationId, 1, DefaultPageSize, cancellationToken);
    }

    // ------------------------------------------------------------------ notifications

    public async Task<PagedResult<NotificationView>> ListNotificationsAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Notification> notifications = context.Notifications
            .AsNoTracking()
            .Where(notification => notification.RecipientUserId == userId);

        int totalCount = await notifications.CountAsync(cancellationToken);
        (int currentPage, int size) = Paging(page, pageSize);

        List<NotificationView> items = await notifications
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .ThenBy(notification => notification.Id)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .Select(notification => new NotificationView(
                notification.Id,
                notification.Kind.ToString(),
                context.CommunityProfiles
                    .Where(profile => profile.UserId == notification.ActorUserId)
                    .Select(profile => profile.Handle)
                    .FirstOrDefault(),
                notification.TargetType,
                notification.TargetId,
                notification.CreatedAtUtc,
                notification.ReadAtUtc != null))
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationView>(
            items,
            currentPage,
            size,
            totalCount,
            size == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)size));
    }

    public async Task<OperationResult> MarkNotificationsReadAsync(
        Guid userId,
        Guid? notificationId,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        List<Notification> targets = await context.Notifications
            .Where(notification => notification.RecipientUserId == userId
                && notification.ReadAtUtc == null
                && (notificationId == null || notification.Id == notificationId))
            .ToListAsync(cancellationToken);

        foreach (Notification notification in targets)
        {
            notification.ReadAtUtc = now;
        }

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    // ------------------------------------------------------------------ helpers

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

    /// <summary>Messaging requires an accepted friendship and an open message setting.</summary>
    private async Task<OperationResult?> CheckCanMessageAsync(
        Guid senderUserId,
        CommunityProfile target,
        CancellationToken cancellationToken)
    {
        if (await IsBlockedEitherWayAsync(senderUserId, target.UserId, cancellationToken))
        {
            return OperationResult.Forbidden(
                ErrorCodes.CommunityBlocked, "You cannot message this member.");
        }

        if (target.MessagePolicy == MessagePolicy.NoOne)
        {
            return OperationResult.Forbidden(
                ErrorCodes.CommunityForbidden, "This member is not accepting messages.");
        }

        return await AreFriendsAsync(senderUserId, target.UserId, cancellationToken)
            ? null
            : OperationResult.Forbidden(
                ErrorCodes.CommunityForbidden,
                "This member accepts messages from friends only.");
    }

    private Task<CommunityProfile?> FindActiveProfileAsync(
        string? handle,
        CancellationToken cancellationToken)
    {
        string normalized = CommunityHandle.Normalize(handle ?? string.Empty);

        return context.CommunityProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(
                profile => profile.NormalizedHandle == normalized
                    && profile.Status == CommunityProfileStatus.Active,
                cancellationToken);
    }

    private Task<bool> AreFriendsAsync(Guid first, Guid second, CancellationToken cancellationToken)
    {
        (Guid low, Guid high) = CanonicalPair.Order(first, second);

        return context.Friendships.AnyAsync(
            friendship => friendship.UserLowId == low && friendship.UserHighId == high,
            cancellationToken);
    }

    private Task<bool> IsBlockedEitherWayAsync(Guid first, Guid second, CancellationToken cancellationToken) =>
        context.UserBlocks.AnyAsync(
            block => (block.BlockerUserId == first && block.BlockedUserId == second)
                || (block.BlockerUserId == second && block.BlockedUserId == first),
            cancellationToken);

    private async Task<HashSet<Guid>> BlockedEitherWayAsync(Guid userId, CancellationToken cancellationToken)
    {
        List<Guid> ids = await context.UserBlocks
            .AsNoTracking()
            .Where(block => block.BlockerUserId == userId || block.BlockedUserId == userId)
            .Select(block => block.BlockerUserId == userId ? block.BlockedUserId : block.BlockerUserId)
            .ToListAsync(cancellationToken);

        return [.. ids];
    }

    private async Task<Dictionary<Guid, string>> HandlesAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken)
    {
        Guid[] ids = userIds.Distinct().ToArray();

        return await context.CommunityProfiles
            .AsNoTracking()
            .Where(profile => ids.Contains(profile.UserId))
            .ToDictionaryAsync(profile => profile.UserId, profile => profile.Handle, cancellationToken);
    }

    private async Task<MemberCard> ToCardAsync(
        Guid viewerUserId,
        CommunityProfile profile,
        CancellationToken cancellationToken)
    {
        bool isFriend = await AreFriendsAsync(viewerUserId, profile.UserId, cancellationToken);
        (Guid low, Guid high) = CanonicalPair.Order(viewerUserId, profile.UserId);

        bool pending = await context.FriendRequests.AnyAsync(
            request => request.UserLowId == low
                && request.UserHighId == high
                && request.Status == FriendRequestStatus.Pending,
            cancellationToken);

        bool hasAvatar = await context.ProfileAvatars.AnyAsync(
            avatar => avatar.UserId == profile.UserId, cancellationToken);

        return new MemberCard(
            profile.UserId,
            profile.Handle,
            profile.Bio,
            hasAvatar,
            isFriend,
            pending,
            profile.FriendRequestPolicy == FriendRequestPolicy.Everyone && !isFriend && !pending,
            profile.MessagePolicy == MessagePolicy.FriendsOnly && isFriend);
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

    private static Dictionary<string, string> PairMetadata(Guid first, Guid second)
    {
        (Guid low, Guid high) = CanonicalPair.Order(first, second);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["userLowId"] = low.ToString("D"),
            ["userHighId"] = high.ToString("D"),
        };
    }

    private static (int Page, int PageSize) Paging(int page, int pageSize) => (
        page < 1 ? 1 : page,
        pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize));

    private async Task<OperationResult<ConversationDetail>> BuildConversationAsync(
        Guid userId,
        Guid conversationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken,
        bool markRead = false)
    {
        DirectConversation? conversation = await context.DirectConversations
            .FirstOrDefaultAsync(candidate => candidate.Id == conversationId, cancellationToken);

        if (conversation is null || !conversation.Includes(userId))
        {
            return OperationResult.NotFound().ToFailure<ConversationDetail>();
        }

        Guid otherUserId = conversation.UserLowId == userId
            ? conversation.UserHighId
            : conversation.UserLowId;

        if (markRead)
        {
            await MarkReadAsync(userId, conversationId, cancellationToken);
        }

        (int currentPage, int size) = Paging(page, pageSize);

        IQueryable<DirectMessage> messages = context.DirectMessages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId);

        int totalCount = await messages.CountAsync(cancellationToken);

        var rows = await messages
            .OrderByDescending(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        List<DirectMessageView> views = rows
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .Select(message => new DirectMessageView(
                message.Id,
                message.SenderUserId == userId,
                message.Status == DirectMessageStatus.Deleted ? string.Empty : message.Body,
                message.Status.ToString(),
                message.Status == DirectMessageStatus.Deleted,
                message.CreatedAtUtc,
                message.EditedAtUtc,
                RowVersionToken.Encode(message.RowVersion)))
            .ToList();

        CommunityProfile? other = await context.CommunityProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(profile => profile.UserId == otherUserId, cancellationToken);

        OperationResult? refusal = other is null
            ? OperationResult.Forbidden(ErrorCodes.CommunityForbidden, "This member is no longer available.")
            : await CheckCanMessageAsync(userId, other, cancellationToken);

        bool hidden = await IsBlockedEitherWayAsync(userId, otherUserId, cancellationToken);

        return OperationResult.FromValue(new ConversationDetail(
            conversationId,
            otherUserId,
            hidden ? "Hidden member" : other?.Handle ?? "Former member",
            refusal is null,
            refusal?.Message,
            new PagedResult<DirectMessageView>(
                views,
                currentPage,
                size,
                totalCount,
                size == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)size))));
    }

    private async Task MarkReadAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        ConversationReadState? state = await context.ConversationReadStates.FirstOrDefaultAsync(
            candidate => candidate.ConversationId == conversationId && candidate.UserId == userId,
            cancellationToken);

        if (state is null)
        {
            context.ConversationReadStates.Add(new ConversationReadState
            {
                ConversationId = conversationId,
                UserId = userId,
                LastReadAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }
        else
        {
            state.LastReadAtUtc = now;
            state.UpdatedAtUtc = now;
        }

        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
    }
}
