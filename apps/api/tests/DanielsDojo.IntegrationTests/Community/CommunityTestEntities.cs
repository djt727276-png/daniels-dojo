using DanielsDojo.Domain.Community;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Database;

namespace DanielsDojo.IntegrationTests.Community;

/// <summary>
/// Builds valid community rows so each test changes exactly the one field it asserts on.
/// Everything defaults to a state the database accepts.
/// </summary>
internal static class CommunityTestEntities
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    public static CommunityProfile Profile(Guid userId, string? handle = null)
    {
        string value = handle ?? $"member{Guid.NewGuid():N}"[..16];

        return new CommunityProfile
        {
            UserId = userId,
            Handle = value,
            NormalizedHandle = value.ToUpperInvariant(),
            Bio = "Test member.",
            IsDiscoverable = true,
            FriendRequestPolicy = FriendRequestPolicy.Everyone,
            MessagePolicy = MessagePolicy.FriendsOnly,
            Status = CommunityProfileStatus.Active,
            GuidelinesVersion = "2026-08",
            GuidelinesAcceptedAtUtc = Now,
            EligibilityAttestedAtUtc = Now,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        };
    }

    public static ForumCategory Category(string? slug = null) => new()
    {
        Id = Guid.NewGuid(),
        Slug = slug ?? $"cat-{Guid.NewGuid():N}",
        Name = "Test Category",
        Description = "Description.",
        SortOrder = 1,
        Status = ForumCategoryStatus.Active,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static ForumThread Thread(Guid categoryId, Guid authorUserId) => new()
    {
        Id = Guid.NewGuid(),
        CategoryId = categoryId,
        AuthorUserId = authorUserId,
        Title = "Test thread",
        Status = ForumThreadStatus.Open,
        IsPinned = false,
        LastActivityAtUtc = Now,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static ForumPost Post(Guid threadId, Guid authorUserId, Guid? replyToPostId = null) => new()
    {
        Id = Guid.NewGuid(),
        ThreadId = threadId,
        AuthorUserId = authorUserId,
        ReplyToPostId = replyToPostId,
        Body = "Test body.",
        Status = ForumPostStatus.Published,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static ForumPostReaction Reaction(Guid postId, Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        PostId = postId,
        UserId = userId,
        ReactionType = ReactionType.Like,
        CreatedAtUtc = Now,
    };

    public static ForumSubscription Subscription(Guid threadId, Guid userId) => new()
    {
        ThreadId = threadId,
        UserId = userId,
        NotificationPreference = ThreadNotificationPreference.AllReplies,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    /// <summary>Builds a request in canonical order with the given requester.</summary>
    public static FriendRequest Request(Guid requester, Guid recipient)
    {
        (Guid low, Guid high) = CanonicalPair.Order(requester, recipient);

        return new FriendRequest
        {
            Id = Guid.NewGuid(),
            UserLowId = low,
            UserHighId = high,
            RequestedByUserId = requester,
            Status = FriendRequestStatus.Pending,
            RequestedAtUtc = Now,
        };
    }

    public static Friendship Friendship(Guid first, Guid second)
    {
        (Guid low, Guid high) = CanonicalPair.Order(first, second);

        return new Friendship
        {
            Id = Guid.NewGuid(),
            UserLowId = low,
            UserHighId = high,
            AcceptedAtUtc = Now,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        };
    }

    public static UserBlock Block(Guid blocker, Guid blocked) => new()
    {
        BlockerUserId = blocker,
        BlockedUserId = blocked,
        ReasonCategory = BlockReasonCategory.Personal,
        CreatedAtUtc = Now,
    };

    public static DirectConversation Conversation(Guid first, Guid second)
    {
        (Guid low, Guid high) = CanonicalPair.Order(first, second);

        return new DirectConversation
        {
            Id = Guid.NewGuid(),
            UserLowId = low,
            UserHighId = high,
            LastMessageAtUtc = Now,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        };
    }

    public static DirectMessage Message(Guid conversationId, Guid senderUserId) => new()
    {
        Id = Guid.NewGuid(),
        ConversationId = conversationId,
        SenderUserId = senderUserId,
        Body = "Hello.",
        Status = DirectMessageStatus.Sent,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static ConversationReadState ReadState(Guid conversationId, Guid userId) => new()
    {
        ConversationId = conversationId,
        UserId = userId,
        LastReadAtUtc = Now,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    public static Notification Notification(Guid recipientUserId, Guid? actorUserId = null) => new()
    {
        Id = Guid.NewGuid(),
        RecipientUserId = recipientUserId,
        ActorUserId = actorUserId,
        Kind = NotificationKind.ThreadReply,
        TargetType = "Thread",
        TargetId = Guid.NewGuid(),
        CreatedAtUtc = Now,
    };

    public static Report Report(Guid reporterUserId, Guid targetId, ReportTargetType targetType) => new()
    {
        Id = Guid.NewGuid(),
        ReporterUserId = reporterUserId,
        TargetType = targetType,
        TargetId = targetId,
        ReasonCode = ReportReasonCode.Spam,
        Detail = "Unsolicited promotion.",
        Status = ReportStatus.Open,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    /// <summary>Creates and saves a platform user, returning its identifier.</summary>
    public static async Task<Guid> CreateUserAsync(DanielsDojoDbContext context)
    {
        User user = TestEntities.User();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }
}
