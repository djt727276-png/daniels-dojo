using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Community;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Privacy;

/// <summary>
/// Data export and account deletion.
/// </summary>
/// <remarks>
/// Export must contain the member's own words and records and nothing anyone else owns.
/// Deletion must follow the documented lifecycle: community presence gone, sent messages
/// tombstoned, posts unattributed, sign-in binding scrubbed so the account is unreachable —
/// while the audit row proves it happened.
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class PrivacyTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private const string Community = "/api/v1/community";

    private ApiHarness _harness = null!;
    private TestActor _member = null!;
    private TestActor _friend = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await SeedCategoryAsync();

        _harness = ApiHarness.Create(fixture);
        _member = await SignedUpMemberAsync("leaving-member");
        _friend = await SignedUpMemberAsync("staying-friend");

        await BefriendAsync();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task ExportContainsTheMembersOwnDataAndNobodyElses()
    {
        // The member writes in both directions; the friend replies.
        Guid conversationId = await StartConversationAsync(_member, "staying-friend");
        await SendMessageAsync(_member, conversationId, "My own words.");
        await SendMessageAsync(_friend, conversationId, "The friend's words.");
        await CreateThreadAsync(_member, "My question", "The body I wrote.");

        using HttpClient client = _harness.CreateClient(_member);
        using JsonDocument export = await client.GetJsonAsync("/api/v1/me/privacy/export");

        JsonElement root = export.RootElement;

        Assert.Equal(
            "leaving-member",
            root.GetProperty("communityProfile").GetProperty("handle").GetString());

        // Sent messages: theirs, and only theirs.
        List<string?> bodies = [.. root.GetProperty("messagesSent").EnumerateArray()
            .Select(message => message.GetProperty("body").GetString())];
        Assert.Contains("My own words.", bodies);
        Assert.DoesNotContain("The friend's words.", bodies);

        Assert.Contains(
            root.GetProperty("forumPosts").EnumerateArray(),
            post => post.GetProperty("body").GetString() == "The body I wrote.");

        // The export itself is audited.
        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.True(await context.AuditLogs.AnyAsync(entry => entry.Action == "privacy.export"));
    }

    [Fact]
    public async Task DeletionFollowsTheDocumentedLifecycle()
    {
        Guid conversationId = await StartConversationAsync(_member, "staying-friend");
        await SendMessageAsync(_member, conversationId, "Soon to be a tombstone.");
        await CreateThreadAsync(_member, "A question that outlives me", "Still useful.");

        using (HttpClient client = _harness.CreateClient(_member))
        {
            // The wrong phrase does nothing.
            using (JsonDocument refused = await client.SendJsonAsync(
                HttpMethod.Post,
                "/api/v1/me/privacy/delete-account",
                new { Confirmation = "yes please" },
                HttpStatusCode.BadRequest))
            {
            }

            using HttpResponseMessage deleted = await client.SendJsonAsync(
                HttpMethod.Post,
                "/api/v1/me/privacy/delete-account",
                new { Confirmation = "delete my account" });
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        await using DanielsDojoDbContext context = fixture.CreateContext();

        // Community presence: gone.
        Assert.False(await context.CommunityProfiles.AnyAsync(
            profile => profile.UserId == _member.UserId));
        Assert.False(await context.Friendships.AnyAsync(
            friendship => friendship.UserLowId == _member.UserId
                || friendship.UserHighId == _member.UserId));

        // Sent messages: tombstoned, body gone, shape kept.
        DirectMessage message = await context.DirectMessages.SingleAsync(
            candidate => candidate.SenderUserId == _member.UserId);
        Assert.Equal(DirectMessageStatus.Deleted, message.Status);
        Assert.Equal(string.Empty, message.Body);

        // Forum content: retained, soon rendered as "Former member".
        Assert.True(await context.ForumPosts.AnyAsync(
            post => post.AuthorUserId == _member.UserId && post.Body == "Still useful."));

        // The account row: scrubbed and unreachable by sign-in.
        User account = await context.Users.SingleAsync(user => user.Id == _member.UserId);
        Assert.Equal("Deleted member", account.DisplayName);
        Assert.Equal(string.Empty, account.Email);
        Assert.StartsWith("deleted:", account.ExternalSubjectId, StringComparison.Ordinal);
        Assert.Equal(UserStatus.Disabled, account.Status);
        Assert.False(await context.UserRoles.AnyAsync(role => role.UserId == _member.UserId));

        Assert.True(await context.AuditLogs.AnyAsync(
            entry => entry.Action == "privacy.account_deleted"));
    }

    [Fact]
    public async Task AfterDeletionTheSamePersonStartsFromNothing()
    {
        using (HttpClient client = _harness.CreateClient(_member))
        {
            using HttpResponseMessage deleted = await client.SendJsonAsync(
                HttpMethod.Post,
                "/api/v1/me/privacy/delete-account",
                new { Confirmation = "delete my account" });
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        // The scrubbed subject no longer maps to the old row, so the same identity is
        // provisioned as a brand-new account with none of the old data attached.
        using HttpClient returning = _harness.CreateClient(_member);
        using HttpResponseMessage profile = await returning.GetAsync(
            new Uri("/api/v1/me/community/profile", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, profile.StatusCode);

        // The session now belongs to a different local account.
        using JsonDocument session = await returning.GetJsonAsync("/api/v1/auth/session");
        Assert.NotEqual(
            _member.UserId, session.RootElement.GetProperty("userId").GetGuid());

        await using DanielsDojoDbContext context = fixture.CreateContext();

        // The scrubbed original survives for its records, unreachable by sign-in.
        User original = await context.Users.SingleAsync(user => user.Id == _member.UserId);
        Assert.Equal(UserStatus.Disabled, original.Status);
        Assert.StartsWith("deleted:", original.ExternalSubjectId, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private async Task BefriendAsync()
    {
        using HttpClient member = _harness.CreateClient(_member);
        using JsonDocument _ = await member.SendJsonAsync(
            HttpMethod.Post,
            $"{Community}/friend-requests",
            new { Handle = "staying-friend" },
            HttpStatusCode.NoContent);

        Guid requestId;

        await using (DanielsDojoDbContext context = fixture.CreateContext())
        {
            requestId = (await context.FriendRequests.SingleAsync()).Id;
        }

        using HttpClient friend = _harness.CreateClient(_friend);
        using JsonDocument __ = await friend.SendJsonAsync(
            HttpMethod.Post,
            $"{Community}/friend-requests/{requestId}/accept",
            null,
            HttpStatusCode.NoContent);
    }

    private async Task<Guid> StartConversationAsync(TestActor actor, string withHandle)
    {
        using HttpClient client = _harness.CreateClient(actor);
        using JsonDocument conversation = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Community}/conversations",
            new { Handle = withHandle },
            HttpStatusCode.OK);

        return conversation.RootElement.GetProperty("id").GetGuid();
    }

    private async Task SendMessageAsync(TestActor actor, Guid conversationId, string body)
    {
        using HttpClient client = _harness.CreateClient(actor);
        using JsonDocument _ = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Community}/conversations/{conversationId}/messages",
            new { Body = body },
            HttpStatusCode.OK);
    }

    private async Task CreateThreadAsync(TestActor actor, string title, string body)
    {
        using HttpClient client = _harness.CreateClient(actor);
        using JsonDocument _ = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Community}/threads",
            new { CategorySlug = "general", Title = title, Body = body },
            HttpStatusCode.OK);
    }

    private async Task<TestActor> SignedUpMemberAsync(string handle)
    {
        TestActor actor = await _harness.SignInAsync();
        using HttpClient client = _harness.CreateClient(actor);

        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/me/community/profile",
            new { Handle = handle, Bio = (string?)null, AcceptGuidelines = true, AttestEligibility = true },
            HttpStatusCode.OK);

        string? rowVersion = created.RootElement.GetProperty("rowVersion").GetString();

        using JsonDocument _ = await client.SendJsonAsync(
            HttpMethod.Put,
            "/api/v1/me/community/profile",
            new
            {
                Bio = (string?)null,
                IsDiscoverable = true,
                FriendRequestPolicy = "Everyone",
                MessagePolicy = "FriendsOnly",
                RowVersion = rowVersion,
            },
            HttpStatusCode.OK);

        return actor;
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
