using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Auditing;
using DanielsDojo.Domain.Community;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Community;

/// <summary>
/// The horizontal-authorization matrix, exercised with three distinct signed-in members.
/// </summary>
/// <remarks>
/// A third real member is the point: refusing a randomly invented identifier proves nothing,
/// because the interesting failure is a caller who is genuinely authenticated and genuinely
/// has a session, but has no business with this particular conversation, message, or post.
/// <para>
/// Every actor here is created inside the test arrangement. The reference and production seed
/// profiles still create zero users and zero community rows.
/// </para>
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class CommunityAuthorizationTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private const string Base = "/api/v1/community";
    private const string Moderation = "/api/v1/admin/community";

    private ApiHarness _harness = null!;

    /// <summary>First party to the private conversation.</summary>
    private TestActor _alice = null!;

    /// <summary>Second party to the private conversation.</summary>
    private TestActor _bob = null!;

    /// <summary>A fully signed-in member with no relationship to either of the other two.</summary>
    private TestActor _carol = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await SeedCategoryAsync();

        _harness = ApiHarness.Create(fixture);
        _alice = await MemberAsync("alice-auth", discoverable: true, openToFriends: true, openMessages: true);
        _bob = await MemberAsync("bob-auth", discoverable: true, openToFriends: true, openMessages: true);
        _carol = await MemberAsync("carol-auth", discoverable: true, openToFriends: true, openMessages: true);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ---------------------------------------------------------------- friendship

    [Fact]
    public async Task AcceptanceCreatesOneCanonicalFriendshipAndCannotBeRepeated()
    {
        Guid requestId = await SendRequestAsync(_alice, "bob-auth");

        await Expect(_bob, HttpMethod.Post, $"{Base}/friend-requests/{requestId}/accept", null, HttpStatusCode.NoContent);

        // Re-accepting an already-answered request is not a second friendship; it is nothing.
        await Expect(_bob, HttpMethod.Post, $"{Base}/friend-requests/{requestId}/accept", null, HttpStatusCode.NotFound);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Friendship friendship = await context.Friendships.SingleAsync();

        // Stored canonically, so one row can only ever mean one relationship.
        Assert.True(
            CanonicalPair.Compare(friendship.UserLowId, friendship.UserHighId) < 0,
            "the friendship pair must be stored in canonical order");

        // A fresh request for the same pair is refused while a friendship exists.
        using JsonDocument problem = await Json(
            _alice, HttpMethod.Post, $"{Base}/friend-requests", new { Handle = "bob-auth" }, HttpStatusCode.Conflict);

        Assert.Equal("platform.duplicate_value", problem.ProblemCode());
    }

    [Fact]
    public async Task EveryPersonalListEndpointActuallyExecutes()
    {
        // Regression guard. These four projections join a pair table to the profile table, a
        // shape the provider could not translate — so each one answered 500 in production
        // while every test looked at the database directly and never noticed.
        await BefriendAsync(_alice, _bob, "bob-auth");
        await SendRequestAsync(_carol, "alice-auth");
        await Expect(
            _alice,
            HttpMethod.Post,
            $"{Base}/blocks",
            new { Handle = "carol-auth", ReasonCategory = "Spam" },
            HttpStatusCode.NoContent);
        await StartConversationAsync(_alice, "bob-auth");

        using JsonDocument friends = await GetJson(_alice, $"{Base}/friends");
        Assert.Equal("bob-auth", friends.RootElement[0].GetProperty("handle").GetString());

        using JsonDocument blocks = await GetJson(_alice, $"{Base}/blocks");
        Assert.Equal("carol-auth", blocks.RootElement[0].GetProperty("handle").GetString());
        Assert.Equal("Spam", blocks.RootElement[0].GetProperty("reasonCategory").GetString());

        using JsonDocument requests = await GetJson(_carol, $"{Base}/friend-requests");
        Assert.Empty(requests.RootElement.EnumerateArray());

        using JsonDocument conversations = await GetJson(_alice, $"{Base}/conversations");
        Assert.Single(conversations.RootElement.EnumerateArray());

        // The member's own screens read the same lists.
        using JsonDocument dashboard = await GetJson(_alice, "/api/v1/me/dashboard");
        Assert.Equal(0, dashboard.RootElement.GetProperty("enrolledCourseCount").GetInt32());

        using JsonDocument courses = await GetJson(_alice, "/api/v1/me/courses");
        Assert.Empty(courses.RootElement.EnumerateArray());
    }

    // ---------------------------------------------------------------- conversations

    [Fact]
    public async Task FriendsReuseExactlyOneCanonicalConversation()
    {
        await BefriendAsync(_alice, _bob, "bob-auth");

        Guid fromAlice = await StartConversationAsync(_alice, "bob-auth");
        Guid againFromAlice = await StartConversationAsync(_alice, "bob-auth");
        Guid fromBob = await StartConversationAsync(_bob, "alice-auth");

        Assert.Equal(fromAlice, againFromAlice);
        Assert.Equal(fromAlice, fromBob);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Equal(1, await context.DirectConversations.CountAsync());
    }

    [Fact]
    public async Task EachParticipantSendsAndOnlyEverUpdatesTheirOwnReadState()
    {
        await BefriendAsync(_alice, _bob, "bob-auth");
        Guid conversationId = await StartConversationAsync(_alice, "bob-auth");

        await SendMessageAsync(_alice, conversationId, "From Alice.");
        await SendMessageAsync(_bob, conversationId, "From Bob.");

        // Opening the conversation marks it read for the caller and nobody else.
        using JsonDocument bobView = await GetJson(_bob, $"{Base}/conversations/{conversationId}");
        Assert.Equal(2, bobView.RootElement.GetProperty("messages").GetProperty("totalCount").GetInt32());

        using JsonDocument bobInbox = await GetJson(_bob, $"{Base}/conversations");
        Assert.Equal(0, bobInbox.RootElement[0].GetProperty("unreadCount").GetInt32());

        using JsonDocument aliceInbox = await GetJson(_alice, $"{Base}/conversations");
        Assert.Equal(1, aliceInbox.RootElement[0].GetProperty("unreadCount").GetInt32());

        await using DanielsDojoDbContext context = fixture.CreateContext();
        List<ConversationReadState> states = await context.ConversationReadStates.ToListAsync();

        Assert.Equal(_bob.UserId, Assert.Single(states).UserId);
    }

    [Fact]
    public async Task NotificationPointersNeverCarryTheMessageBody()
    {
        await BefriendAsync(_alice, _bob, "bob-auth");
        Guid conversationId = await StartConversationAsync(_alice, "bob-auth");

        const string Secret = "A-PRIVATE-SENTENCE-ONLY-BOB-SHOULD-READ";
        await SendMessageAsync(_alice, conversationId, Secret);

        using HttpResponseMessage response = await _harness.CreateClient(_bob)
            .GetAsync(new Uri("/api/v1/me/notifications", UriKind.Relative));

        string body = await response.Content.ReadAsStringAsync();

        Assert.Contains("DirectMessage", body, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- third party

    [Fact]
    public async Task AThirdMemberIsRefusedEveryPartOfSomeoneElsesConversation()
    {
        await BefriendAsync(_alice, _bob, "bob-auth");
        Guid conversationId = await StartConversationAsync(_alice, "bob-auth");
        Guid messageId = await SendMessageAsync(_alice, conversationId, "Between Alice and Bob only.");

        // Carol is signed in, community-ready, and completely uninvolved. Every route reports
        // "not found" rather than "forbidden", so she cannot even confirm the id exists.
        await Expect(_carol, HttpMethod.Get, $"{Base}/conversations/{conversationId}", null, HttpStatusCode.NotFound);
        await Expect(_carol, HttpMethod.Post, $"{Base}/conversations/{conversationId}/messages", new { Body = "Butting in." }, HttpStatusCode.NotFound);
        await Expect(_carol, HttpMethod.Delete, $"{Base}/messages/{messageId}", null, HttpStatusCode.NotFound);

        // Her own conversation list stays empty, and the read state is untouched.
        using JsonDocument carolInbox = await GetJson(_carol, $"{Base}/conversations");
        Assert.Empty(carolInbox.RootElement.EnumerateArray());

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.DoesNotContain(
            await context.ConversationReadStates.Select(state => state.UserId).ToListAsync(),
            id => id == _carol.UserId);

        // And the message itself is untouched.
        DirectMessage stored = await context.DirectMessages.SingleAsync(message => message.Id == messageId);
        Assert.Equal(DirectMessageStatus.Sent, stored.Status);
    }

    [Fact]
    public async Task AThirdMemberCannotEditOrRemoveAnotherMembersPost()
    {
        (Guid threadId, Guid postId, string rowVersion) = await ThreadAsync(_alice, "Alice's thread");

        await Expect(
            _carol,
            HttpMethod.Put,
            $"{Base}/posts/{postId}",
            new { Body = "Rewritten by a stranger.", RowVersion = rowVersion },
            HttpStatusCode.NotFound);

        await Expect(_carol, HttpMethod.Delete, $"{Base}/posts/{postId}", null, HttpStatusCode.NotFound);

        using JsonDocument thread = await GetJson(_alice, $"{Base}/threads/{threadId}");
        Assert.Equal(
            "Alice's thread body.",
            thread.RootElement.GetProperty("posts").GetProperty("items")[0].GetProperty("body").GetString());
    }

    [Fact]
    public async Task AStudentCannotReachAnyAdminCommunityRouteByDirectCall()
    {
        (_, Guid postId, _) = await ThreadAsync(_alice, "Escalation attempt");
        Guid reportId = Guid.NewGuid();

        foreach ((HttpMethod method, string path, object? payload) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Get, $"{Moderation}/reports", null),
            (HttpMethod.Get, $"{Moderation}/reports/{reportId}/target", null),
            (HttpMethod.Post, $"{Moderation}/reports/{reportId}/status/Resolved", new { Reason = "Mine now.", RowVersion = "AAAAAAAAAAE=" }),
            (HttpMethod.Post, $"{Moderation}/posts/{postId}/remove", new { Reason = "Mine now." }),
            (HttpMethod.Post, $"{Moderation}/threads/{Guid.NewGuid()}/status/Locked", new { Reason = "Mine now." }),
            (HttpMethod.Post, $"{Moderation}/threads/{Guid.NewGuid()}/pin", new { Pinned = true, Reason = "Mine now." }),
            (HttpMethod.Post, $"{Moderation}/profiles/{_alice.UserId}/status/Suspended", new { Reason = "Mine now." }),
        })
        {
            await Expect(_carol, method, path, payload, HttpStatusCode.Forbidden);
        }

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Equal(ForumPostStatus.Published, (await context.ForumPosts.SingleAsync(post => post.Id == postId)).Status);
    }

    // ---------------------------------------------------------------- blocks

    [Fact]
    public async Task BlockingEndsTheFriendshipAndCancelsPendingRequestsInOneTransaction()
    {
        await BefriendAsync(_alice, _bob, "bob-auth");
        await SendRequestAsync(_carol, "alice-auth");

        await Expect(
            _alice,
            HttpMethod.Post,
            $"{Base}/blocks",
            new { Handle = "carol-auth", ReasonCategory = "Harassment" },
            HttpStatusCode.NoContent);

        await using DanielsDojoDbContext context = fixture.CreateContext();

        // Carol's pending request is cancelled; the unrelated Alice/Bob friendship survives.
        FriendRequest cancelled = await context.FriendRequests.SingleAsync(
            request => request.RequestedByUserId == _carol.UserId);

        Assert.Equal(FriendRequestStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.RespondedAtUtc);
        Assert.False(
            await context.FriendRequests.AnyAsync(request => request.Status == FriendRequestStatus.Pending),
            "no request may be left pending across a block");
        Assert.Equal(1, await context.Friendships.CountAsync());

        await Expect(
            _alice, HttpMethod.Post, $"{Base}/blocks", new { Handle = "bob-auth", ReasonCategory = "Personal" }, HttpStatusCode.NoContent);

        Assert.Equal(0, await context.Friendships.CountAsync());
    }

    [Fact]
    public async Task ABlockClosesEveryDirectionOfContact()
    {
        await BefriendAsync(_alice, _bob, "bob-auth");
        Guid conversationId = await StartConversationAsync(_alice, "bob-auth");
        await SendMessageAsync(_alice, conversationId, "Before the block.");

        int notificationsBeforeBlock = await CountNotificationsAsync();

        await Expect(
            _alice, HttpMethod.Post, $"{Base}/blocks", new { Handle = "bob-auth", ReasonCategory = "Personal" }, HttpStatusCode.NoContent);

        // Discovery: gone in both directions, and reported as absent rather than refused.
        Assert.Empty((await GetJson(_alice, $"{Base}/people?search=bob-")).RootElement.EnumerateArray());
        Assert.Empty((await GetJson(_bob, $"{Base}/people?search=alice-")).RootElement.EnumerateArray());
        await Expect(_alice, HttpMethod.Get, $"{Base}/people/bob-auth", null, HttpStatusCode.NotFound);
        await Expect(_bob, HttpMethod.Get, $"{Base}/people/alice-auth", null, HttpStatusCode.NotFound);

        // Friend requests: refused both ways with the same message a closed setting produces.
        await Expect(_alice, HttpMethod.Post, $"{Base}/friend-requests", new { Handle = "bob-auth" }, HttpStatusCode.Forbidden);
        await Expect(_bob, HttpMethod.Post, $"{Base}/friend-requests", new { Handle = "alice-auth" }, HttpStatusCode.Forbidden);

        // Conversation reuse and new messages: refused both ways.
        await Expect(_alice, HttpMethod.Post, $"{Base}/conversations", new { Handle = "bob-auth" }, HttpStatusCode.Forbidden);
        await Expect(_bob, HttpMethod.Post, $"{Base}/conversations", new { Handle = "alice-auth" }, HttpStatusCode.Forbidden);
        await Expect(_alice, HttpMethod.Post, $"{Base}/conversations/{conversationId}/messages", new { Body = "After the block." }, HttpStatusCode.Forbidden);
        await Expect(_bob, HttpMethod.Post, $"{Base}/conversations/{conversationId}/messages", new { Body = "After the block." }, HttpStatusCode.Forbidden);

        // The existing conversation is readable but visibly closed, and the other party's
        // handle is not disclosed.
        using JsonDocument view = await GetJson(_bob, $"{Base}/conversations/{conversationId}");
        Assert.False(view.RootElement.GetProperty("canSend").GetBoolean());
        Assert.Equal("Hidden member", view.RootElement.GetProperty("otherHandle").GetString());

        // Not one of the refused interactions produced a notification, so a block cannot be
        // used to keep pinging someone who has stepped away.
        Assert.Equal(notificationsBeforeBlock, await CountNotificationsAsync());
    }

    [Fact]
    public async Task UnblockingRestoresNeitherTheFriendshipNorMessaging()
    {
        await BefriendAsync(_alice, _bob, "bob-auth");
        Guid conversationId = await StartConversationAsync(_alice, "bob-auth");

        await Expect(
            _alice, HttpMethod.Post, $"{Base}/blocks", new { Handle = "bob-auth", ReasonCategory = "Personal" }, HttpStatusCode.NoContent);
        await Expect(_alice, HttpMethod.Delete, $"{Base}/blocks/{_bob.UserId}", null, HttpStatusCode.NoContent);

        await using DanielsDojoDbContext context = fixture.CreateContext();

        // The friendship is not resurrected by lifting the block.
        Assert.Equal(0, await context.Friendships.CountAsync());

        // And messaging stays closed, because friends-only still means friends.
        using JsonDocument problem = await Json(
            _alice,
            HttpMethod.Post,
            $"{Base}/conversations/{conversationId}/messages",
            new { Body = "Back again." },
            HttpStatusCode.Forbidden);

        Assert.Equal("community.forbidden", problem.ProblemCode());
    }

    [Fact]
    public async Task EverySendReChecksTheCurrentRelationship()
    {
        await BefriendAsync(_alice, _bob, "bob-auth");
        Guid conversationId = await StartConversationAsync(_alice, "bob-auth");
        await SendMessageAsync(_alice, conversationId, "While we were friends.");

        // Unfriending alone — no block — must close the conversation on the very next send,
        // rather than being trusted because a conversation row already exists.
        await Expect(_bob, HttpMethod.Delete, $"{Base}/friends/{_alice.UserId}", null, HttpStatusCode.NoContent);

        await Expect(
            _alice,
            HttpMethod.Post,
            $"{Base}/conversations/{conversationId}/messages",
            new { Body = "After unfriending." },
            HttpStatusCode.Forbidden);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Equal(1, await context.DirectMessages.CountAsync());
    }

    [Fact]
    public async Task ClosingTheMessageSettingStopsTheNextSend()
    {
        await BefriendAsync(_alice, _bob, "bob-auth");
        Guid conversationId = await StartConversationAsync(_alice, "bob-auth");
        await SendMessageAsync(_alice, conversationId, "While messages were open.");

        await SetPrivacyAsync(_bob, discoverable: true, openToFriends: true, openMessages: false);

        using JsonDocument problem = await Json(
            _alice,
            HttpMethod.Post,
            $"{Base}/conversations/{conversationId}/messages",
            new { Body = "After they closed messages." },
            HttpStatusCode.Forbidden);

        Assert.Equal("community.forbidden", problem.ProblemCode());
    }

    // ---------------------------------------------------------------- moderation reach

    [Fact]
    public async Task AReportedMessageIsReadableOnlyThroughTheAuditedReportFlow()
    {
        await BefriendAsync(_alice, _bob, "bob-auth");
        Guid conversationId = await StartConversationAsync(_alice, "bob-auth");

        const string Reported = "THE-REPORTED-SENTENCE";
        Guid messageId = await SendMessageAsync(_alice, conversationId, Reported);

        await Expect(
            _bob,
            HttpMethod.Post,
            $"{Base}/reports",
            new { TargetType = "Message", TargetId = messageId, ReasonCode = "Harassment", Detail = "Please look at this." },
            HttpStatusCode.Accepted);

        TestActor moderator = await _harness.SignInAsync(admin: true);

        // The moderator has no route to the conversation itself, only to the report.
        await Expect(moderator, HttpMethod.Get, $"{Base}/conversations/{conversationId}", null, HttpStatusCode.NotFound);
        Assert.Empty((await GetJson(moderator, $"{Base}/conversations")).RootElement.EnumerateArray());

        using JsonDocument queue = await GetJson(moderator, $"{Moderation}/reports?status=Open");
        JsonElement report = queue.RootElement.GetProperty("items")[0];
        Guid reportId = report.GetProperty("id").GetGuid();

        // The queue itself lists the target but never quotes it.
        Assert.DoesNotContain(Reported, queue.RootElement.GetRawText(), StringComparison.Ordinal);

        using JsonDocument target = await GetJson(moderator, $"{Moderation}/reports/{reportId}/target");
        Assert.Equal("Message", target.RootElement.GetProperty("targetType").GetString());
        Assert.Equal(Reported, target.RootElement.GetProperty("content").GetString());
        Assert.Equal("alice-auth", target.RootElement.GetProperty("authorHandle").GetString());

        // Exactly the reported message and nothing around it.
        Assert.DoesNotContain(
            conversationId.ToString("D"), target.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        AuditLog view = await context.AuditLogs.SingleAsync(
            entry => entry.Action == "Community.Report.TargetViewed");

        Assert.Equal(moderator.UserId, view.ActorUserId);
        Assert.DoesNotContain(Reported, view.MetadataJson, StringComparison.Ordinal);

        // Resolving is reasoned and audited, still without copying the content.
        using JsonDocument resolved = await Json(
            moderator,
            HttpMethod.Post,
            $"{Moderation}/reports/{reportId}/status/Resolved",
            new { Reason = "Warned the sender.", RowVersion = report.GetProperty("rowVersion").GetString() },
            HttpStatusCode.OK);

        Assert.Equal("Resolved", resolved.RootElement.GetProperty("status").GetString());

        AuditLog decision = await context.AuditLogs.SingleAsync(
            entry => entry.Action == "Community.Report.Decided");
        Assert.Equal("Warned the sender.", decision.Reason);
        Assert.DoesNotContain(Reported, decision.MetadataJson, StringComparison.Ordinal);

        // Once decided, the content is locked again.
        await Expect(moderator, HttpMethod.Get, $"{Moderation}/reports/{reportId}/target", null, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AModeratorCannotOpenAMessageThatWasNeverReported()
    {
        await BefriendAsync(_alice, _bob, "bob-auth");
        Guid conversationId = await StartConversationAsync(_alice, "bob-auth");
        await SendMessageAsync(_alice, conversationId, "Nobody reported this.");

        TestActor moderator = await _harness.SignInAsync(admin: true);

        // No report, no route: an invented report id opens nothing.
        await Expect(moderator, HttpMethod.Get, $"{Moderation}/reports/{Guid.NewGuid()}/target", null, HttpStatusCode.NotFound);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.False(await context.AuditLogs.AnyAsync(entry => entry.Action == "Community.Report.TargetViewed"));
    }

    // ---------------------------------------------------------------- helpers

    private HttpClient Client(TestActor actor) => _harness.CreateClient(actor);

    private async Task<int> CountNotificationsAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        return await context.Notifications.CountAsync();
    }

    private async Task<JsonDocument> GetJson(TestActor actor, string path)
    {
        using HttpClient client = Client(actor);

        return await client.GetJsonAsync(path);
    }

    private async Task<JsonDocument> Json(
        TestActor actor,
        HttpMethod method,
        string path,
        object? payload,
        HttpStatusCode expected)
    {
        using HttpClient client = Client(actor);

        return await client.SendJsonAsync(method, path, payload, expected);
    }

    private async Task Expect(
        TestActor actor,
        HttpMethod method,
        string path,
        object? payload,
        HttpStatusCode expected)
    {
        using HttpClient client = Client(actor);
        using HttpResponseMessage response = payload is null && method == HttpMethod.Get
            ? await client.GetAsync(new Uri(path, UriKind.Relative))
            : await client.SendJsonAsync(method, path, payload);

        string body = await response.Content.ReadAsStringAsync();

        Assert.True(
            expected == response.StatusCode,
            $"{method} {path} expected {expected} but returned {response.StatusCode}: {body}");
    }

    private async Task<Guid> SendRequestAsync(TestActor sender, string handle)
    {
        await Expect(sender, HttpMethod.Post, $"{Base}/friend-requests", new { Handle = handle }, HttpStatusCode.NoContent);

        await using DanielsDojoDbContext context = fixture.CreateContext();

        return await context.FriendRequests
            .Where(request => request.RequestedByUserId == sender.UserId
                && request.Status == FriendRequestStatus.Pending)
            .Select(request => request.Id)
            .SingleAsync();
    }

    private async Task BefriendAsync(TestActor sender, TestActor recipient, string recipientHandle)
    {
        Guid requestId = await SendRequestAsync(sender, recipientHandle);

        await Expect(recipient, HttpMethod.Post, $"{Base}/friend-requests/{requestId}/accept", null, HttpStatusCode.NoContent);
    }

    private async Task<Guid> StartConversationAsync(TestActor actor, string handle)
    {
        using JsonDocument conversation = await Json(
            actor, HttpMethod.Post, $"{Base}/conversations", new { Handle = handle }, HttpStatusCode.OK);

        return conversation.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> SendMessageAsync(TestActor actor, Guid conversationId, string body)
    {
        using JsonDocument sent = await Json(
            actor,
            HttpMethod.Post,
            $"{Base}/conversations/{conversationId}/messages",
            new { Body = body },
            HttpStatusCode.OK);

        return sent.RootElement.GetProperty("messages").GetProperty("items")
            .EnumerateArray()
            .Last()
            .GetProperty("id")
            .GetGuid();
    }

    private async Task<(Guid ThreadId, Guid PostId, string RowVersion)> ThreadAsync(TestActor author, string title)
    {
        using JsonDocument thread = await Json(
            author,
            HttpMethod.Post,
            $"{Base}/threads",
            new { CategorySlug = "general", Title = title, Body = "Alice's thread body." },
            HttpStatusCode.OK);

        JsonElement first = thread.RootElement.GetProperty("posts").GetProperty("items")[0];

        return (
            thread.RootElement.GetProperty("id").GetGuid(),
            first.GetProperty("id").GetGuid(),
            first.GetProperty("rowVersion").GetString()!);
    }

    private async Task<TestActor> MemberAsync(
        string handle,
        bool discoverable,
        bool openToFriends,
        bool openMessages)
    {
        TestActor actor = await _harness.SignInAsync();
        using HttpClient client = Client(actor);

        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/me/community/profile",
            new { Handle = handle, Bio = (string?)null, AcceptGuidelines = true, AttestEligibility = true },
            HttpStatusCode.OK);

        await SetPrivacyAsync(
            actor,
            discoverable,
            openToFriends,
            openMessages,
            created.RootElement.GetProperty("rowVersion").GetString());

        return actor;
    }

    private async Task SetPrivacyAsync(
        TestActor actor,
        bool discoverable,
        bool openToFriends,
        bool openMessages,
        string? rowVersion = null)
    {
        using HttpClient client = Client(actor);

        if (rowVersion is null)
        {
            using JsonDocument current = await client.GetJsonAsync("/api/v1/me/community/profile");
            rowVersion = current.RootElement.GetProperty("rowVersion").GetString();
        }

        using JsonDocument _ = await client.SendJsonAsync(
            HttpMethod.Put,
            "/api/v1/me/community/profile",
            new
            {
                Bio = (string?)null,
                IsDiscoverable = discoverable,
                FriendRequestPolicy = openToFriends ? "Everyone" : "NoOne",
                MessagePolicy = openMessages ? "FriendsOnly" : "NoOne",
                RowVersion = rowVersion,
            },
            HttpStatusCode.OK);
    }

    private async Task SeedCategoryAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        if (await context.ForumCategories.AnyAsync(category => category.Slug == "general"))
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        context.ForumCategories.Add(new ForumCategory
        {
            Id = Guid.NewGuid(),
            Slug = "general",
            Name = "General",
            Description = "Anything about the platform.",
            SortOrder = 0,
            Status = ForumCategoryStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        await context.SaveChangesAsync();
    }
}
