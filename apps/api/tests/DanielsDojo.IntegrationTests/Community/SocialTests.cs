using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Community;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Community;

/// <summary>
/// Exercises discovery, friend requests, blocks, direct messages, and notifications — with
/// particular attention to the privacy defaults and to blocks being honoured in both
/// directions.
/// </summary>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class SocialTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private const string Base = "/api/v1/community";

    private ApiHarness _harness = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _harness = ApiHarness.Create(fixture);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ---------------------------------------------------------------- discovery

    [Fact]
    public async Task ADefaultProfileIsNotDiscoverable()
    {
        TestActor searcher = await MemberAsync("searcher");
        await MemberAsync("private-person");

        using HttpClient client = _harness.CreateClient(searcher);
        using JsonDocument results = await client.GetJsonAsync($"{Base}/people?search=private");

        Assert.Empty(results.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task OnlyMembersWhoOptedIntoDiscoveryAppear()
    {
        TestActor searcher = await MemberAsync("finder");
        TestActor found = await MemberAsync("discoverable-one", discoverable: true);

        using HttpClient client = _harness.CreateClient(searcher);
        using JsonDocument results = await client.GetJsonAsync($"{Base}/people?search=discover");

        JsonElement card = Assert.Single(results.RootElement.EnumerateArray().ToArray());
        Assert.Equal("discoverable-one", card.GetProperty("handle").GetString());

        // Only a handle and a bio. Nothing from the identity provider is exposed.
        Assert.False(card.TryGetProperty("email", out _));
        Assert.False(card.TryGetProperty("displayName", out _));

        // Contact is still closed, because discovery and contact are separate choices.
        Assert.False(card.GetProperty("canReceiveFriendRequests").GetBoolean());
        Assert.Equal(found.UserId, card.GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task AShortSearchReturnsNothingRatherThanEveryone()
    {
        TestActor searcher = await MemberAsync("browser");
        await MemberAsync("aardvark", discoverable: true);

        using HttpClient client = _harness.CreateClient(searcher);
        using JsonDocument results = await client.GetJsonAsync($"{Base}/people?search=a");

        Assert.Empty(results.RootElement.EnumerateArray());
    }

    // ---------------------------------------------------------------- friend requests

    [Fact]
    public async Task AClosedProfileRefusesFriendRequests()
    {
        TestActor sender = await MemberAsync("hopeful");
        await MemberAsync("closed-door", discoverable: true);

        using HttpClient client = _harness.CreateClient(sender);

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/friend-requests",
            new { Handle = "closed-door" },
            HttpStatusCode.Forbidden);

        Assert.Equal("community.forbidden", problem.ProblemCode());
    }

    [Fact]
    public async Task AcceptingARequestCreatesExactlyOneFriendship()
    {
        TestActor sender = await MemberAsync("sender");
        TestActor recipient = await MemberAsync("recipient", discoverable: true, openToFriends: true);

        await SendFriendRequestAsync(sender, "recipient");

        using HttpClient recipientClient = _harness.CreateClient(recipient);
        using JsonDocument requests = await recipientClient.GetJsonAsync($"{Base}/friend-requests");

        JsonElement pending = Assert.Single(requests.RootElement.EnumerateArray().ToArray());
        Assert.True(pending.GetProperty("incoming").GetBoolean());

        Guid requestId = pending.GetProperty("id").GetGuid();

        using HttpResponseMessage accepted = await recipientClient.SendJsonAsync(
            HttpMethod.Post, $"{Base}/friend-requests/{requestId}/accept", payload: null);

        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Equal(1, await context.Friendships.CountAsync());

        // The sender is told, so the acceptance is not silent.
        Assert.True(await context.Notifications.AnyAsync(notification =>
            notification.RecipientUserId == sender.UserId
            && notification.Kind == NotificationKind.FriendAccepted));
    }

    [Fact]
    public async Task ASenderCannotAcceptTheirOwnRequest()
    {
        TestActor sender = await MemberAsync("self-accepter");
        await MemberAsync("target-member", discoverable: true, openToFriends: true);

        await SendFriendRequestAsync(sender, "target-member");

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Guid requestId = await context.FriendRequests.Select(request => request.Id).SingleAsync();

        using HttpClient client = _harness.CreateClient(sender);
        using HttpResponseMessage response = await client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/friend-requests/{requestId}/accept", payload: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await context.Friendships.CountAsync());
    }

    [Fact]
    public async Task OnlyOneRequestCanBePendingForAPair()
    {
        TestActor sender = await MemberAsync("persistent");
        await MemberAsync("patient-one", discoverable: true, openToFriends: true);

        await SendFriendRequestAsync(sender, "patient-one");

        using HttpClient client = _harness.CreateClient(sender);
        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/friend-requests",
            new { Handle = "patient-one" },
            HttpStatusCode.Conflict);

        Assert.Equal("platform.duplicate_value", problem.ProblemCode());
    }

    // ---------------------------------------------------------------- blocks

    [Fact]
    public async Task BlockingEndsTheFriendshipAndStopsFurtherContact()
    {
        (TestActor first, TestActor second) = await FriendsAsync("blocker-one", "blocked-one");

        using HttpClient client = _harness.CreateClient(first);

        using HttpResponseMessage blocked = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/blocks",
            new { Handle = "blocked-one", ReasonCategory = "Personal" });

        Assert.Equal(HttpStatusCode.NoContent, blocked.StatusCode);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Equal(0, await context.Friendships.CountAsync());

        // The blocked member cannot get back in touch either — the block is symmetric.
        using HttpClient other = _harness.CreateClient(second);
        using JsonDocument problem = await other.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/conversations",
            new { Handle = "blocker-one" },
            HttpStatusCode.Forbidden);

        Assert.Equal("community.blocked", problem.ProblemCode());
    }

    [Fact]
    public async Task ABlockedMemberDisappearsFromSearch()
    {
        TestActor viewer = await MemberAsync("viewer");
        await MemberAsync("nuisance", discoverable: true);

        using HttpClient client = _harness.CreateClient(viewer);
        await client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/blocks", new { Handle = "nuisance", ReasonCategory = "Spam" });

        using JsonDocument results = await client.GetJsonAsync($"{Base}/people?search=nuis");
        Assert.Empty(results.RootElement.EnumerateArray());

        using HttpResponseMessage direct =
            await client.GetAsync(new Uri($"{Base}/people/nuisance", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, direct.StatusCode);
    }

    // ---------------------------------------------------------------- messages

    [Fact]
    public async Task MessagingRequiresFriendshipAndAnOpenSetting()
    {
        TestActor sender = await MemberAsync("would-be-sender");
        await MemberAsync("unreachable", discoverable: true, openToFriends: true);

        using HttpClient client = _harness.CreateClient(sender);

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/conversations",
            new { Handle = "unreachable" },
            HttpStatusCode.Forbidden);

        Assert.Equal("community.forbidden", problem.ProblemCode());
    }

    [Fact]
    public async Task FriendsCanExchangeMessagesAndDeleteTheirOwn()
    {
        (TestActor first, TestActor second) = await FriendsAsync("chatty-one", "chatty-two", openMessages: true);

        using HttpClient client = _harness.CreateClient(first);

        using JsonDocument conversation = await client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/conversations", new { Handle = "chatty-two" }, HttpStatusCode.OK);

        Guid conversationId = conversation.RootElement.GetProperty("id").GetGuid();
        Assert.True(conversation.RootElement.GetProperty("canSend").GetBoolean());

        using JsonDocument sent = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/conversations/{conversationId}/messages",
            new { Body = "Hello there." },
            HttpStatusCode.OK);

        JsonElement message = sent.RootElement.GetProperty("messages").GetProperty("items")[0];
        Assert.Equal("Hello there.", message.GetProperty("body").GetString());
        Guid messageId = message.GetProperty("id").GetGuid();

        // The recipient sees it and it counts as unread until they open the conversation.
        using HttpClient recipient = _harness.CreateClient(second);
        using JsonDocument inbox = await recipient.GetJsonAsync($"{Base}/conversations");
        Assert.Equal(1, inbox.RootElement[0].GetProperty("unreadCount").GetInt32());

        using JsonDocument opened =
            await recipient.GetJsonAsync($"{Base}/conversations/{conversationId}");
        Assert.Single(opened.RootElement.GetProperty("messages").GetProperty("items").EnumerateArray());

        using JsonDocument afterRead = await recipient.GetJsonAsync($"{Base}/conversations");
        Assert.Equal(0, afterRead.RootElement[0].GetProperty("unreadCount").GetInt32());

        // Deleting tombstones the row and clears the text from the database.
        using JsonDocument deleted = await client.SendJsonAsync(
            HttpMethod.Delete, $"{Base}/messages/{messageId}", payload: null, HttpStatusCode.OK);

        JsonElement tombstone = deleted.RootElement.GetProperty("messages").GetProperty("items")[0];
        Assert.True(tombstone.GetProperty("withheld").GetBoolean());
        Assert.Equal(string.Empty, tombstone.GetProperty("body").GetString());

        await using DanielsDojoDbContext context = fixture.CreateContext();
        DirectMessage stored = await context.DirectMessages.SingleAsync(entry => entry.Id == messageId);
        Assert.Equal(DirectMessageStatus.Deleted, stored.Status);
        Assert.Equal(string.Empty, stored.Body);
    }

    [Fact]
    public async Task AConversationIsInvisibleToEveryoneElse()
    {
        (TestActor first, _) = await FriendsAsync("insider-one", "insider-two", openMessages: true);

        using HttpClient client = _harness.CreateClient(first);
        using JsonDocument conversation = await client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/conversations", new { Handle = "insider-two" }, HttpStatusCode.OK);

        Guid conversationId = conversation.RootElement.GetProperty("id").GetGuid();

        TestActor outsider = await MemberAsync("outsider");
        using HttpClient outsiderClient = _harness.CreateClient(outsider);

        using HttpResponseMessage response = await outsiderClient.GetAsync(
            new Uri($"{Base}/conversations/{conversationId}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------- notifications

    [Fact]
    public async Task NotificationsCarryAPointerAndNeverContent()
    {
        (TestActor first, TestActor second) = await FriendsAsync("noisy-one", "noisy-two", openMessages: true);

        using HttpClient sender = _harness.CreateClient(first);
        using JsonDocument conversation = await sender.SendJsonAsync(
            HttpMethod.Post, $"{Base}/conversations", new { Handle = "noisy-two" }, HttpStatusCode.OK);

        await sender.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/conversations/{conversation.RootElement.GetProperty("id").GetGuid()}/messages",
            new { Body = "A private and identifiable sentence." },
            HttpStatusCode.OK);

        using HttpClient recipient = _harness.CreateClient(second);
        using JsonDocument notifications = await recipient.GetJsonAsync("/api/v1/me/notifications");

        string body = notifications.RootElement.GetRawText();
        Assert.Contains("DirectMessage", body, StringComparison.Ordinal);
        Assert.DoesNotContain("A private and identifiable sentence.", body, StringComparison.Ordinal);

        JsonElement entry = notifications.RootElement.GetProperty("items")[0];
        Assert.False(entry.GetProperty("read").GetBoolean());

        using HttpResponseMessage marked = await recipient.SendJsonAsync(
            HttpMethod.Put, "/api/v1/me/notifications/read", new { NotificationId = (Guid?)null });

        Assert.Equal(HttpStatusCode.NoContent, marked.StatusCode);

        using JsonDocument after = await recipient.GetJsonAsync("/api/v1/me/notifications");
        Assert.True(after.RootElement.GetProperty("items")[0].GetProperty("read").GetBoolean());
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Signs a member in, completes setup, and applies the requested privacy settings.</summary>
    private async Task<TestActor> MemberAsync(
        string handle,
        bool discoverable = false,
        bool openToFriends = false,
        bool openMessages = false)
    {
        TestActor actor = await _harness.SignInAsync();
        using HttpClient client = _harness.CreateClient(actor);

        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/me/community/profile",
            new { Handle = handle, Bio = (string?)null, AcceptGuidelines = true, AttestEligibility = true },
            HttpStatusCode.OK);

        if (discoverable || openToFriends || openMessages)
        {
            await client.SendJsonAsync(
                HttpMethod.Put,
                "/api/v1/me/community/profile",
                new
                {
                    Bio = (string?)null,
                    IsDiscoverable = discoverable,
                    FriendRequestPolicy = openToFriends ? "Everyone" : "NoOne",
                    MessagePolicy = openMessages ? "FriendsOnly" : "NoOne",
                    RowVersion = created.RootElement.GetProperty("rowVersion").GetString(),
                },
                HttpStatusCode.OK);
        }

        return actor;
    }

    private async Task SendFriendRequestAsync(TestActor sender, string handle)
    {
        using HttpClient client = _harness.CreateClient(sender);

        using HttpResponseMessage response = await client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/friend-requests", new { Handle = handle });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>Creates two members and an accepted friendship between them.</summary>
    private async Task<(TestActor First, TestActor Second)> FriendsAsync(
        string firstHandle,
        string secondHandle,
        bool openMessages = false)
    {
        TestActor first = await MemberAsync(firstHandle, discoverable: true, openMessages: openMessages);
        TestActor second = await MemberAsync(
            secondHandle, discoverable: true, openToFriends: true, openMessages: openMessages);

        await SendFriendRequestAsync(first, secondHandle);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Guid requestId = await context.FriendRequests
            .Where(request => request.Status == FriendRequestStatus.Pending)
            .Select(request => request.Id)
            .SingleAsync();

        using HttpClient client = _harness.CreateClient(second);
        await client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/friend-requests/{requestId}/accept", payload: null);

        return (first, second);
    }
}
