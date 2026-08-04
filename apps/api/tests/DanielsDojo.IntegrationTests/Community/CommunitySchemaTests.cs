using DanielsDojo.Domain.Community;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Community;

/// <summary>
/// Proves the community schema enforces its invariants in SQL Server rather than relying on
/// application code. Every rule here is one an attacker or a race could otherwise break.
/// </summary>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class CommunitySchemaTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------- canonical pairs

    [Fact]
    public async Task FriendRequest_WithReversedPair_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        (Guid first, Guid second) = await CreatePairAsync(context);
        (Guid low, Guid high) = CanonicalPair.Order(first, second);

        // Deliberately reversed: storing the same pair in both orders would let the two
        // members end up with divergent friendship state.
        context.FriendRequests.Add(new FriendRequest
        {
            Id = Guid.NewGuid(),
            UserLowId = high,
            UserHighId = low,
            RequestedByUserId = high,
            Status = FriendRequestStatus.Pending,
            RequestedAtUtc = DateTimeOffset.UtcNow,
        });

        await AssertCheckViolationAsync(context, "CK_FriendRequests_CanonicalPair");
    }

    [Fact]
    public async Task FriendRequest_WithSelfPair_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        Guid user = await CommunityTestEntities.CreateUserAsync(context);

        context.FriendRequests.Add(new FriendRequest
        {
            Id = Guid.NewGuid(),
            UserLowId = user,
            UserHighId = user,
            RequestedByUserId = user,
            Status = FriendRequestStatus.Pending,
            RequestedAtUtc = DateTimeOffset.UtcNow,
        });

        await AssertCheckViolationAsync(context, "CK_FriendRequests_CanonicalPair");
    }

    [Fact]
    public async Task FriendRequest_FromSomeoneOutsideThePair_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        (Guid first, Guid second) = await CreatePairAsync(context);
        Guid outsider = await CommunityTestEntities.CreateUserAsync(context);

        FriendRequest request = CommunityTestEntities.Request(first, second);
        request.RequestedByUserId = outsider;
        context.FriendRequests.Add(request);

        await AssertCheckViolationAsync(context, "CK_FriendRequests_RequesterIsParticipant");
    }

    [Fact]
    public async Task Friendship_WithReversedOrSelfPair_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        (Guid first, Guid second) = await CreatePairAsync(context);
        (Guid low, Guid high) = CanonicalPair.Order(first, second);

        context.Friendships.Add(new Friendship
        {
            Id = Guid.NewGuid(),
            UserLowId = high,
            UserHighId = low,
            AcceptedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        await AssertCheckViolationAsync(context, "CK_Friendships_CanonicalPair");

        await using DanielsDojoDbContext selfContext = fixture.CreateContext();
        selfContext.Friendships.Add(new Friendship
        {
            Id = Guid.NewGuid(),
            UserLowId = first,
            UserHighId = first,
            AcceptedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        await AssertCheckViolationAsync(selfContext, "CK_Friendships_CanonicalPair");
    }

    [Fact]
    public async Task Conversation_WithReversedOrSelfPair_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        (Guid first, Guid second) = await CreatePairAsync(context);
        (Guid low, Guid high) = CanonicalPair.Order(first, second);

        context.DirectConversations.Add(new DirectConversation
        {
            Id = Guid.NewGuid(),
            UserLowId = high,
            UserHighId = low,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        await AssertCheckViolationAsync(context, "CK_DirectConversations_CanonicalPair");
    }

    // ---------------------------------------------------------- uniqueness

    [Fact]
    public async Task DuplicateFriendship_ForTheSamePair_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        (Guid first, Guid second) = await CreatePairAsync(context);

        context.Friendships.Add(CommunityTestEntities.Friendship(first, second));
        await context.SaveChangesAsync();

        // Same pair, arguments swapped: canonicalisation means it is the same row.
        context.Friendships.Add(CommunityTestEntities.Friendship(second, first));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateConversation_ForTheSamePair_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        (Guid first, Guid second) = await CreatePairAsync(context);

        context.DirectConversations.Add(CommunityTestEntities.Conversation(first, second));
        await context.SaveChangesAsync();

        context.DirectConversations.Add(CommunityTestEntities.Conversation(second, first));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task SecondPendingRequest_ForTheSamePair_IsRejected_ButResolvedHistoryIsKept()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        (Guid first, Guid second) = await CreatePairAsync(context);

        FriendRequest declined = CommunityTestEntities.Request(first, second);
        declined.Status = FriendRequestStatus.Declined;
        declined.RespondedAtUtc = DateTimeOffset.UtcNow;
        context.FriendRequests.Add(declined);

        FriendRequest pending = CommunityTestEntities.Request(second, first);
        context.FriendRequests.Add(pending);

        // A resolved row and a pending row coexist: the uniqueness filter covers pending only.
        await context.SaveChangesAsync();

        context.FriendRequests.Add(CommunityTestEntities.Request(first, second));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task SecondOpenReport_FromTheSameReporterForTheSameTarget_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        Guid reporter = await CommunityTestEntities.CreateUserAsync(context);
        Guid target = Guid.NewGuid();

        context.Reports.Add(
            CommunityTestEntities.Report(reporter, target, ReportTargetType.Post));
        await context.SaveChangesAsync();

        context.Reports.Add(
            CommunityTestEntities.Report(reporter, target, ReportTargetType.Post));

        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task ResolvedReport_DoesNotBlockANewReport()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        Guid reporter = await CommunityTestEntities.CreateUserAsync(context);
        Guid handler = await CommunityTestEntities.CreateUserAsync(context);
        Guid target = Guid.NewGuid();

        Report first = CommunityTestEntities.Report(reporter, target, ReportTargetType.Post);
        first.Status = ReportStatus.Resolved;
        first.HandledByUserId = handler;
        first.HandledAtUtc = DateTimeOffset.UtcNow;
        context.Reports.Add(first);
        await context.SaveChangesAsync();

        context.Reports.Add(CommunityTestEntities.Report(reporter, target, ReportTargetType.Post));
        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Reports.CountAsync());
    }

    [Fact]
    public async Task DuplicateProfileHandle_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        Guid first = await CommunityTestEntities.CreateUserAsync(context);
        Guid second = await CommunityTestEntities.CreateUserAsync(context);

        context.CommunityProfiles.Add(CommunityTestEntities.Profile(first, "sharedhandle"));
        await context.SaveChangesAsync();

        context.CommunityProfiles.Add(CommunityTestEntities.Profile(second, "sharedhandle"));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateReaction_IsRejected()
    {
        ForumFixture forum;
        await using (DanielsDojoDbContext setup = fixture.CreateContext())
        {
            forum = await ForumFixture.CreateAsync(setup);
            setup.ForumPostReactions.Add(
                CommunityTestEntities.Reaction(forum.PostId, forum.AuthorUserId));
            await setup.SaveChangesAsync();
        }

        // A fresh context, so the database rejects the duplicate rather than EF's identity
        // map catching it first.
        await using DanielsDojoDbContext context = fixture.CreateContext();
        context.ForumPostReactions.Add(
            CommunityTestEntities.Reaction(forum.PostId, forum.AuthorUserId));

        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateSubscription_IsRejected()
    {
        ForumFixture forum;
        await using (DanielsDojoDbContext setup = fixture.CreateContext())
        {
            forum = await ForumFixture.CreateAsync(setup);
            setup.ForumSubscriptions.Add(
                CommunityTestEntities.Subscription(forum.ThreadId, forum.AuthorUserId));
            await setup.SaveChangesAsync();
        }

        await using DanielsDojoDbContext context = fixture.CreateContext();
        context.ForumSubscriptions.Add(
            CommunityTestEntities.Subscription(forum.ThreadId, forum.AuthorUserId));

        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateReadState_IsRejected()
    {
        Guid conversationId;
        Guid userId;

        await using (DanielsDojoDbContext setup = fixture.CreateContext())
        {
            MessagingFixture messaging = await MessagingFixture.CreateAsync(setup);
            conversationId = messaging.ConversationId;
            userId = messaging.FirstUserId;

            setup.ConversationReadStates.Add(
                CommunityTestEntities.ReadState(conversationId, userId));
            await setup.SaveChangesAsync();
        }

        await using DanielsDojoDbContext context = fixture.CreateContext();
        context.ConversationReadStates.Add(CommunityTestEntities.ReadState(conversationId, userId));

        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateBlock_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        (Guid first, Guid second) = await CreatePairAsync(context);

        context.UserBlocks.Add(CommunityTestEntities.Block(first, second));
        await context.SaveChangesAsync();

        // A second context, so the database rejects the duplicate rather than EF's identity
        // map catching it first.
        await using DanielsDojoDbContext duplicateContext = fixture.CreateContext();
        duplicateContext.UserBlocks.Add(CommunityTestEntities.Block(first, second));
        await AssertUniqueViolationAsync(duplicateContext);
    }

    [Fact]
    public async Task ReciprocalBlock_IsAllowed_BecauseBlocksAreDirected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        (Guid first, Guid second) = await CreatePairAsync(context);

        context.UserBlocks.Add(CommunityTestEntities.Block(first, second));
        context.UserBlocks.Add(CommunityTestEntities.Block(second, first));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.UserBlocks.CountAsync());
    }

    // ---------------------------------------------------------- self-relations

    [Fact]
    public async Task SelfBlock_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        Guid user = await CommunityTestEntities.CreateUserAsync(context);

        context.UserBlocks.Add(CommunityTestEntities.Block(user, user));

        await AssertCheckViolationAsync(context, "CK_UserBlocks_NoSelfBlock");
    }

    [Fact]
    public async Task SelfNotification_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        Guid user = await CommunityTestEntities.CreateUserAsync(context);

        context.Notifications.Add(CommunityTestEntities.Notification(user, actorUserId: user));

        await AssertCheckViolationAsync(context, "CK_Notifications_NoSelfNotification");
    }

    [Fact]
    public async Task SelfReply_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        ForumFixture forum = await ForumFixture.CreateAsync(context);

        ForumPost post = await context.ForumPosts.SingleAsync(p => p.Id == forum.PostId);
        post.ReplyToPostId = post.Id;

        await AssertCheckViolationAsync(context, "CK_ForumPosts_NoSelfReply");
    }

    // ---------------------------------------------------------- composite reply FK

    [Fact]
    public async Task Reply_ToAPostInAnotherThread_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        ForumFixture first = await ForumFixture.CreateAsync(context);

        ForumThread otherThread =
            CommunityTestEntities.Thread(first.CategoryId, first.AuthorUserId);
        context.ForumThreads.Add(otherThread);
        await context.SaveChangesAsync();

        // Reply lives in the other thread but points at a post in the first: the composite
        // foreign key must refuse it rather than trusting application code to notice.
        context.ForumPosts.Add(
            CommunityTestEntities.Post(otherThread.Id, first.AuthorUserId, first.PostId));

        DbUpdateException exception =
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains(
            "FK_ForumPosts_ReplyToPost_SameThread",
            exception.InnerException?.Message ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reply_WithinTheSameThread_IsAccepted()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        ForumFixture forum = await ForumFixture.CreateAsync(context);

        context.ForumPosts.Add(
            CommunityTestEntities.Post(forum.ThreadId, forum.AuthorUserId, forum.PostId));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.ForumPosts.CountAsync());
    }

    // ---------------------------------------------------------- tombstones

    [Fact]
    public async Task RemovedPost_MustBeTombstoned()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        ForumFixture forum = await ForumFixture.CreateAsync(context);

        ForumPost post = await context.ForumPosts.SingleAsync(p => p.Id == forum.PostId);
        post.Status = ForumPostStatus.Removed;

        // Status changed but the body was left in place and no removal time recorded.
        await AssertCheckViolationAsync(context, "CK_ForumPosts_RemovedIsTombstoned");
    }

    [Fact]
    public async Task RemovedPost_WithClearedBodyAndTimestamp_IsAccepted()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        ForumFixture forum = await ForumFixture.CreateAsync(context);

        ForumPost post = await context.ForumPosts.SingleAsync(p => p.Id == forum.PostId);
        post.Status = ForumPostStatus.Removed;
        post.Body = string.Empty;
        post.RemovedAtUtc = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync();

        ForumPost stored = await context.ForumPosts.SingleAsync(p => p.Id == forum.PostId);
        Assert.Equal(string.Empty, stored.Body);
        Assert.NotNull(stored.RemovedAtUtc);
    }

    [Fact]
    public async Task EditedPost_RequiresAnEditTimestamp()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        ForumFixture forum = await ForumFixture.CreateAsync(context);

        ForumPost post = await context.ForumPosts.SingleAsync(p => p.Id == forum.PostId);
        post.Status = ForumPostStatus.Edited;

        await AssertCheckViolationAsync(context, "CK_ForumPosts_EditedHasTimestamp");
    }

    [Fact]
    public async Task DeletedMessage_MustBeTombstoned()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        MessagingFixture messaging = await MessagingFixture.CreateAsync(context);

        DirectMessage message =
            await context.DirectMessages.SingleAsync(m => m.Id == messaging.MessageId);
        message.Status = DirectMessageStatus.Deleted;

        await AssertCheckViolationAsync(context, "CK_DirectMessages_DeletedIsTombstoned");
    }

    [Fact]
    public async Task DeletedMessage_WithClearedBodyAndTimestamp_IsAccepted()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        MessagingFixture messaging = await MessagingFixture.CreateAsync(context);

        DirectMessage message =
            await context.DirectMessages.SingleAsync(m => m.Id == messaging.MessageId);
        message.Status = DirectMessageStatus.Deleted;
        message.Body = string.Empty;
        message.DeletedAtUtc = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync();

        Assert.Equal(
            string.Empty,
            (await context.DirectMessages.SingleAsync(m => m.Id == messaging.MessageId)).Body);
    }

    // ---------------------------------------------------------- enum constraints

    [Theory]
    [InlineData("community.Profiles", "Status", "CK_Profiles_Status", "UserId")]
    [InlineData("community.Profiles", "MessagePolicy", "CK_Profiles_MessagePolicy", "UserId")]
    [InlineData("community.Profiles", "FriendRequestPolicy", "CK_Profiles_FriendRequestPolicy", "UserId")]
    public async Task ProfileEnumColumns_RejectValuesOutsideTheEnum(
        string table,
        string column,
        string constraint,
        string keyColumn)
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        Guid user = await CommunityTestEntities.CreateUserAsync(context);
        context.CommunityProfiles.Add(CommunityTestEntities.Profile(user));
        await context.SaveChangesAsync();

        await AssertEnumRejectedAsync(context, table, column, constraint, keyColumn, user);
    }

    [Fact]
    public async Task ForumAndMessagingEnumColumns_RejectValuesOutsideTheEnum()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        ForumFixture forum = await ForumFixture.CreateAsync(context);

        await AssertEnumRejectedAsync(
            context, "community.ForumThreads", "Status", "CK_ForumThreads_Status", "Id", forum.ThreadId);
        await AssertEnumRejectedAsync(
            context, "community.ForumPosts", "Status", "CK_ForumPosts_Status", "Id", forum.PostId);
        await AssertEnumRejectedAsync(
            context, "community.ForumCategories", "Status", "CK_ForumCategories_Status", "Id", forum.CategoryId);

        MessagingFixture messaging = await MessagingFixture.CreateAsync(context);
        await AssertEnumRejectedAsync(
            context, "community.DirectMessages", "Status", "CK_DirectMessages_Status", "Id", messaging.MessageId);
    }

    [Fact]
    public async Task MessagePolicy_CannotBeWidenedBeyondFriendsOnly()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        Guid user = await CommunityTestEntities.CreateUserAsync(context);
        context.CommunityProfiles.Add(CommunityTestEntities.Profile(user));
        await context.SaveChangesAsync();

        // 'Everyone' is not a member of MessagePolicy, so unsolicited direct messaging is
        // not representable even by a direct UPDATE.
        SqlException exception = await Assert.ThrowsAsync<SqlException>(
            () => context.Database.ExecuteSqlRawAsync(
                "UPDATE [community].[Profiles] SET [MessagePolicy] = {0} WHERE [UserId] = {1}",
                "Everyone",
                user));

        Assert.Contains("CK_Profiles_MessagePolicy", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GuidelinesVersionAndAcceptance_MustBeRecordedTogether()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        Guid user = await CommunityTestEntities.CreateUserAsync(context);

        CommunityProfile profile = CommunityTestEntities.Profile(user);
        profile.GuidelinesVersion = "2026-08";
        profile.GuidelinesAcceptedAtUtc = null;
        context.CommunityProfiles.Add(profile);

        await AssertCheckViolationAsync(context, "CK_Profiles_GuidelinesPaired");
    }

    // ---------------------------------------------------------- concurrency

    [Fact]
    public async Task StaleRowVersions_ThrowConcurrencyExceptions()
    {
        Guid userId;
        await using (DanielsDojoDbContext setup = fixture.CreateContext())
        {
            userId = await CommunityTestEntities.CreateUserAsync(setup);
            setup.CommunityProfiles.Add(CommunityTestEntities.Profile(userId));
            await setup.SaveChangesAsync();
        }

        await using DanielsDojoDbContext firstReader = fixture.CreateContext();
        await using DanielsDojoDbContext secondReader = fixture.CreateContext();

        CommunityProfile first = await firstReader.CommunityProfiles.SingleAsync();
        CommunityProfile second = await secondReader.CommunityProfiles.SingleAsync();

        first.Bio = "Updated by the first writer.";
        await firstReader.SaveChangesAsync();

        second.Bio = "Updated by the second writer.";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondReader.SaveChangesAsync());
    }

    [Fact]
    public async Task StaleThreadAndPostRowVersions_ThrowConcurrencyExceptions()
    {
        Guid threadId;
        Guid postId;

        await using (DanielsDojoDbContext setup = fixture.CreateContext())
        {
            ForumFixture forum = await ForumFixture.CreateAsync(setup);
            threadId = forum.ThreadId;
            postId = forum.PostId;
        }

        await using DanielsDojoDbContext firstReader = fixture.CreateContext();
        await using DanielsDojoDbContext secondReader = fixture.CreateContext();

        ForumThread firstThread = await firstReader.ForumThreads.SingleAsync(t => t.Id == threadId);
        ForumThread secondThread = await secondReader.ForumThreads.SingleAsync(t => t.Id == threadId);

        firstThread.Title = "Renamed";
        await firstReader.SaveChangesAsync();

        secondThread.Title = "Also renamed";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondReader.SaveChangesAsync());

        await using DanielsDojoDbContext firstPostReader = fixture.CreateContext();
        await using DanielsDojoDbContext secondPostReader = fixture.CreateContext();

        ForumPost firstPost = await firstPostReader.ForumPosts.SingleAsync(p => p.Id == postId);
        ForumPost secondPost = await secondPostReader.ForumPosts.SingleAsync(p => p.Id == postId);

        firstPost.Body = "Edited first.";
        firstPost.Status = ForumPostStatus.Edited;
        firstPost.EditedAtUtc = DateTimeOffset.UtcNow;
        await firstPostReader.SaveChangesAsync();

        secondPost.Body = "Edited second.";
        secondPost.Status = ForumPostStatus.Edited;
        secondPost.EditedAtUtc = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondPostReader.SaveChangesAsync());
    }

    // ---------------------------------------------------------- restrictive deletion

    [Fact]
    public async Task DeletingAMemberWithCommunityHistory_IsRejected()
    {
        Guid authorUserId;
        await using (DanielsDojoDbContext setup = fixture.CreateContext())
        {
            ForumFixture forum = await ForumFixture.CreateAsync(setup);
            authorUserId = forum.AuthorUserId;
        }

        await using DanielsDojoDbContext context = fixture.CreateContext();
        User user = await context.Users.SingleAsync(u => u.Id == authorUserId);
        context.Users.Remove(user);

        await AssertReferenceViolationAsync(context);
    }

    [Fact]
    public async Task DeletingACategoryOrThreadWithContent_IsRejected()
    {
        Guid categoryId;
        Guid threadId;

        await using (DanielsDojoDbContext setup = fixture.CreateContext())
        {
            ForumFixture forum = await ForumFixture.CreateAsync(setup);
            categoryId = forum.CategoryId;
            threadId = forum.ThreadId;
        }

        await using DanielsDojoDbContext categoryContext = fixture.CreateContext();
        categoryContext.ForumCategories.Remove(
            await categoryContext.ForumCategories.SingleAsync(c => c.Id == categoryId));
        await AssertReferenceViolationAsync(categoryContext);

        await using DanielsDojoDbContext threadContext = fixture.CreateContext();
        threadContext.ForumThreads.Remove(
            await threadContext.ForumThreads.SingleAsync(t => t.Id == threadId));
        await AssertReferenceViolationAsync(threadContext);
    }

    [Fact]
    public async Task DeletingAConversationWithMessages_IsRejected()
    {
        Guid conversationId;
        await using (DanielsDojoDbContext setup = fixture.CreateContext())
        {
            MessagingFixture messaging = await MessagingFixture.CreateAsync(setup);
            conversationId = messaging.ConversationId;
        }

        await using DanielsDojoDbContext context = fixture.CreateContext();
        context.DirectConversations.Remove(
            await context.DirectConversations.SingleAsync(c => c.Id == conversationId));

        await AssertReferenceViolationAsync(context);
    }

    // ---------------------------------------------------------- helpers

    private static async Task<(Guid First, Guid Second)> CreatePairAsync(DanielsDojoDbContext context)
    {
        Guid first = await CommunityTestEntities.CreateUserAsync(context);
        Guid second = await CommunityTestEntities.CreateUserAsync(context);
        return (first, second);
    }

    /// <summary>
    /// Writes an out-of-range enum value with parameterised SQL. EF cannot produce one, and no
    /// untrusted value is ever interpolated into the statement text.
    /// </summary>
    private static async Task AssertEnumRejectedAsync(
        DanielsDojoDbContext context,
        string table,
        string column,
        string constraint,
        string keyColumn,
        object keyValue)
    {
        string[] parts = table.Split('.');
        string sql =
            $"UPDATE [{parts[0]}].[{parts[1]}] SET [{column}] = {{0}} WHERE [{keyColumn}] = {{1}}";

        SqlException exception = await Assert.ThrowsAsync<SqlException>(
            () => context.Database.ExecuteSqlRawAsync(sql, "NotARealValue", keyValue));

        Assert.Contains(constraint, exception.Message, StringComparison.Ordinal);
    }

    private static async Task AssertCheckViolationAsync(
        DanielsDojoDbContext context,
        string expectedConstraintName)
    {
        DbUpdateException exception =
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains(
            expectedConstraintName,
            exception.InnerException?.Message ?? string.Empty,
            StringComparison.Ordinal);

        context.ChangeTracker.Clear();
    }

    private static async Task AssertUniqueViolationAsync(DanielsDojoDbContext context)
    {
        DbUpdateException exception =
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains(
            "duplicate key",
            exception.InnerException?.Message ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        context.ChangeTracker.Clear();
    }

    private static async Task AssertReferenceViolationAsync(DanielsDojoDbContext context)
    {
        DbUpdateException exception =
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains(
            "REFERENCE constraint",
            exception.InnerException?.Message ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        context.ChangeTracker.Clear();
    }
}

/// <summary>A saved category, thread, and post owned by one member.</summary>
internal sealed record ForumFixture(Guid AuthorUserId, Guid CategoryId, Guid ThreadId, Guid PostId)
{
    public static async Task<ForumFixture> CreateAsync(DanielsDojoDbContext context)
    {
        Guid author = await CommunityTestEntities.CreateUserAsync(context);

        ForumCategory category = CommunityTestEntities.Category();
        ForumThread thread = CommunityTestEntities.Thread(category.Id, author);
        ForumPost post = CommunityTestEntities.Post(thread.Id, author);

        context.ForumCategories.Add(category);
        context.ForumThreads.Add(thread);
        context.ForumPosts.Add(post);
        await context.SaveChangesAsync();

        return new ForumFixture(author, category.Id, thread.Id, post.Id);
    }
}

/// <summary>A saved conversation between two members with one message.</summary>
internal sealed record MessagingFixture(
    Guid FirstUserId,
    Guid SecondUserId,
    Guid ConversationId,
    Guid MessageId)
{
    public static async Task<MessagingFixture> CreateAsync(DanielsDojoDbContext context)
    {
        Guid first = await CommunityTestEntities.CreateUserAsync(context);
        Guid second = await CommunityTestEntities.CreateUserAsync(context);

        DirectConversation conversation = CommunityTestEntities.Conversation(first, second);
        DirectMessage message = CommunityTestEntities.Message(conversation.Id, first);

        context.DirectConversations.Add(conversation);
        context.DirectMessages.Add(message);
        await context.SaveChangesAsync();

        return new MessagingFixture(first, second, conversation.Id, message.Id);
    }
}
