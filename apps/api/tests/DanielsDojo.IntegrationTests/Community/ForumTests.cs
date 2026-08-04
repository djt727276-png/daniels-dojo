using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Community;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Community;

/// <summary>
/// Exercises the forum: the participation gate, plain-text bodies, tombstoned removals,
/// reactions, subscriptions and notifications, blocks, and moderation.
/// </summary>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class ForumTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private const string Base = "/api/v1/community";
    private const string Moderation = "/api/v1/admin/community";

    private ApiHarness _harness = null!;
    private TestActor _author = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await SeedCategoryAsync();

        _harness = ApiHarness.Create(fixture);
        _author = await SignedUpMemberAsync("thread-author");
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ---------------------------------------------------------------- participation gate

    [Fact]
    public async Task WritingBeforeSetup_Is403WithTheSetupCode()
    {
        TestActor newcomer = await _harness.SignInAsync();
        using HttpClient client = _harness.CreateClient(newcomer);

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/threads",
            NewThread("Trying to post"),
            HttpStatusCode.Forbidden);

        Assert.Equal("community.setup_required", problem.ProblemCode());
    }

    [Fact]
    public async Task ReadingIsAllowedBeforeSetup()
    {
        TestActor newcomer = await _harness.SignInAsync();
        using HttpClient client = _harness.CreateClient(newcomer);

        using JsonDocument categories = await client.GetJsonAsync($"{Base}/categories");

        Assert.NotEmpty(categories.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task SuspendedMember_CannotPost()
    {
        using HttpClient client = _harness.CreateClient(_author);

        await using (DanielsDojoDbContext context = fixture.CreateContext())
        {
            CommunityProfile profile =
                await context.CommunityProfiles.SingleAsync(entry => entry.UserId == _author.UserId);
            profile.Status = CommunityProfileStatus.Suspended;
            await context.SaveChangesAsync();
        }

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/threads", NewThread("Suspended"), HttpStatusCode.Forbidden);

        Assert.Equal("community.forbidden", problem.ProblemCode());
    }

    // ---------------------------------------------------------------- threads and posts

    [Fact]
    public async Task CreatingAThread_StoresTheBodyAsPlainTextAndSubscribesTheAuthor()
    {
        using HttpClient client = _harness.CreateClient(_author);

        const string Hostile = "<img src=x onerror=alert(1)><script>alert(2)</script>";

        using JsonDocument thread = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/threads",
            new { CategorySlug = "general", Title = "Hello", Body = Hostile },
            HttpStatusCode.OK);

        Assert.True(thread.RootElement.GetProperty("subscribed").GetBoolean());

        // Stored and returned verbatim. Nothing sanitises it, because nothing ever renders it
        // as markup — the client binds it as text.
        JsonElement first = thread.RootElement.GetProperty("posts").GetProperty("items")[0];
        Assert.Equal(Hostile, first.GetProperty("body").GetString());
    }

    [Fact]
    public async Task ReplyingUpdatesActivityAndNotifiesTheSubscribedAuthor()
    {
        Guid threadId = await CreateThreadAsync(_author, "Notify me");

        TestActor responder = await SignedUpMemberAsync("responder");
        using HttpClient client = _harness.CreateClient(responder);

        await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/threads/{threadId}/posts",
            new { Body = "Good point.", ReplyToPostId = (Guid?)null },
            HttpStatusCode.OK);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Notification notification = await context.Notifications
            .SingleAsync(entry => entry.RecipientUserId == _author.UserId);

        Assert.Equal(NotificationKind.ThreadReply, notification.Kind);
        Assert.Equal(responder.UserId, notification.ActorUserId);

        // The notification points at the post and carries none of its text.
        Assert.Equal("Post", notification.TargetType);
    }

    [Fact]
    public async Task ReplyingToAPostFromAnotherThread_IsRefused()
    {
        Guid first = await CreateThreadAsync(_author, "First");
        Guid second = await CreateThreadAsync(_author, "Second");

        using HttpClient client = _harness.CreateClient(_author);
        using JsonDocument other = await client.GetJsonAsync($"{Base}/threads/{first}");
        Guid foreignPostId = other.RootElement
            .GetProperty("posts").GetProperty("items")[0].GetProperty("id").GetGuid();

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/threads/{second}/posts",
            new { Body = "Cross-thread reply.", ReplyToPostId = foreignPostId },
            HttpStatusCode.BadRequest);

        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("replyToPostId", out _));
    }

    [Fact]
    public async Task EditingSomeoneElsesPost_LooksLikeItDoesNotExist()
    {
        Guid threadId = await CreateThreadAsync(_author, "Not yours");
        Guid postId = await FirstPostIdAsync(_author, threadId);
        string rowVersion = await FirstPostRowVersionAsync(_author, threadId);

        TestActor other = await SignedUpMemberAsync("interloper");
        using HttpClient client = _harness.CreateClient(other);

        using HttpResponseMessage response = await client.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/posts/{postId}",
            new { Body = "Rewritten.", RowVersion = rowVersion });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemovingAPost_TombstonesItRatherThanDeletingTheRow()
    {
        Guid threadId = await CreateThreadAsync(_author, "Tombstone me");
        Guid postId = await FirstPostIdAsync(_author, threadId);

        using HttpClient client = _harness.CreateClient(_author);
        using HttpResponseMessage response = await client.SendJsonAsync(
            HttpMethod.Delete, $"{Base}/posts/{postId}", payload: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        ForumPost stored = await context.ForumPosts.SingleAsync(post => post.Id == postId);

        Assert.Equal(ForumPostStatus.Removed, stored.Status);
        Assert.Equal(string.Empty, stored.Body);
        Assert.NotNull(stored.RemovedAtUtc);

        // The reader sees a placeholder, and the withheld text never reaches the browser.
        using JsonDocument thread = await client.GetJsonAsync($"{Base}/threads/{threadId}");
        JsonElement post = thread.RootElement.GetProperty("posts").GetProperty("items")[0];
        Assert.True(post.GetProperty("withheld").GetBoolean());
        Assert.Equal(string.Empty, post.GetProperty("body").GetString());
    }

    // ---------------------------------------------------------------- reactions

    [Fact]
    public async Task LikingIsIdempotentAndNotifiesTheAuthorOnce()
    {
        Guid threadId = await CreateThreadAsync(_author, "Like me");
        Guid postId = await FirstPostIdAsync(_author, threadId);

        TestActor fan = await SignedUpMemberAsync("enthusiast");
        using HttpClient client = _harness.CreateClient(fan);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            await client.SendJsonAsync(
                HttpMethod.Put, $"{Base}/posts/{postId}/reaction", new { Liked = true }, HttpStatusCode.OK);
        }

        using JsonDocument thread = await client.GetJsonAsync($"{Base}/threads/{threadId}");
        JsonElement post = thread.RootElement.GetProperty("posts").GetProperty("items")[0];

        Assert.Equal(1, post.GetProperty("likeCount").GetInt32());
        Assert.True(post.GetProperty("likedByMe").GetBoolean());

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Equal(
            1,
            await context.Notifications.CountAsync(entry =>
                entry.RecipientUserId == _author.UserId && entry.Kind == NotificationKind.PostReaction));

        // Removing the like takes the count back down.
        await client.SendJsonAsync(
            HttpMethod.Put, $"{Base}/posts/{postId}/reaction", new { Liked = false }, HttpStatusCode.OK);

        using JsonDocument after = await client.GetJsonAsync($"{Base}/threads/{threadId}");
        Assert.Equal(
            0,
            after.RootElement.GetProperty("posts").GetProperty("items")[0]
                .GetProperty("likeCount").GetInt32());
    }

    // ---------------------------------------------------------------- blocks

    [Fact]
    public async Task ABlockedAuthorsPostIsWithheldFromTheReader()
    {
        Guid threadId = await CreateThreadAsync(_author, "Blocked view");

        TestActor reader = await SignedUpMemberAsync("blocker");

        await using (DanielsDojoDbContext context = fixture.CreateContext())
        {
            context.UserBlocks.Add(new UserBlock
            {
                BlockerUserId = reader.UserId,
                BlockedUserId = _author.UserId,
                ReasonCategory = BlockReasonCategory.Personal,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });

            await context.SaveChangesAsync();
        }

        using HttpClient client = _harness.CreateClient(reader);
        using JsonDocument thread = await client.GetJsonAsync($"{Base}/threads/{threadId}");
        JsonElement post = thread.RootElement.GetProperty("posts").GetProperty("items")[0];

        Assert.True(post.GetProperty("withheld").GetBoolean());
        Assert.True(post.GetProperty("authorHidden").GetBoolean());
        Assert.Equal(string.Empty, post.GetProperty("body").GetString());
        Assert.Equal("Hidden member", post.GetProperty("authorHandle").GetString());

        // The handle is not disclosed anywhere in the payload either.
        Assert.DoesNotContain("thread-author", thread.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- reports

    [Fact]
    public async Task ReportingTwice_IsRefusedWhileTheFirstIsStillOpen()
    {
        Guid threadId = await CreateThreadAsync(_author, "Report me");
        Guid postId = await FirstPostIdAsync(_author, threadId);

        TestActor reporter = await SignedUpMemberAsync("reporter");
        using HttpClient client = _harness.CreateClient(reporter);

        using HttpResponseMessage first = await client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/reports", Report(postId));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/reports", Report(postId), HttpStatusCode.Conflict);

        Assert.Equal("platform.duplicate_value", problem.ProblemCode());
    }

    // ---------------------------------------------------------------- moderation

    [Fact]
    public async Task ModerationIsAdminOnly()
    {
        using HttpClient client = _harness.CreateClient(_author);

        using HttpResponseMessage response =
            await client.GetAsync(new Uri($"{Moderation}/reports", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ModeratorRemovalTombstonesTheContentAndRecordsTheReason()
    {
        Guid threadId = await CreateThreadAsync(_author, "Moderate me");
        Guid postId = await FirstPostIdAsync(_author, threadId);

        TestActor moderator = await _harness.SignInAsync(admin: true);
        using HttpClient client = _harness.CreateClient(moderator);

        using HttpResponseMessage response = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Moderation}/posts/{postId}/remove",
            new { Reason = "Breached the guidelines on harassment." });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        ForumPost stored = await context.ForumPosts.SingleAsync(post => post.Id == postId);
        Assert.Equal(ForumPostStatus.Removed, stored.Status);
        Assert.Equal(string.Empty, stored.Body);

        var entry = await context.AuditLogs.SingleAsync(log =>
            log.TargetId == postId.ToString("D") && log.Action == "Community.Post.RemovedByModerator");

        Assert.Equal("Breached the guidelines on harassment.", entry.Reason);
        Assert.Equal(moderator.UserId, entry.ActorUserId);

        // The author is told a decision was taken, without the moderator's private note.
        Notification notice = await context.Notifications.SingleAsync(item =>
            item.RecipientUserId == _author.UserId && item.Kind == NotificationKind.Moderation);
        Assert.Null(notice.ActorUserId);
    }

    [Fact]
    public async Task ModerationWithoutAReason_IsRefused()
    {
        Guid threadId = await CreateThreadAsync(_author, "No reason");
        Guid postId = await FirstPostIdAsync(_author, threadId);

        TestActor moderator = await _harness.SignInAsync(admin: true);
        using HttpClient client = _harness.CreateClient(moderator);

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Moderation}/posts/{postId}/remove",
            new { Reason = "   " },
            HttpStatusCode.BadRequest);

        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("reason", out _));
    }

    [Fact]
    public async Task LockingAThreadStopsRepliesButKeepsItReadable()
    {
        Guid threadId = await CreateThreadAsync(_author, "Lock me");

        TestActor moderator = await _harness.SignInAsync(admin: true);
        using HttpClient moderatorClient = _harness.CreateClient(moderator);

        await moderatorClient.SendJsonAsync(
            HttpMethod.Post,
            $"{Moderation}/threads/{threadId}/status/Locked",
            new { Reason = "Discussion went in circles." });

        using HttpClient memberClient = _harness.CreateClient(_author);

        using JsonDocument problem = await memberClient.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/threads/{threadId}/posts",
            new { Body = "One more thing.", ReplyToPostId = (Guid?)null },
            HttpStatusCode.Conflict);

        Assert.Equal("community.forbidden", problem.ProblemCode());

        using JsonDocument readable = await memberClient.GetJsonAsync($"{Base}/threads/{threadId}");
        Assert.Equal("Locked", readable.RootElement.GetProperty("status").GetString());
        Assert.False(readable.RootElement.GetProperty("acceptsReplies").GetBoolean());
    }

    [Fact]
    public async Task ARemovedThreadIsIndistinguishableFromOneThatNeverExisted()
    {
        Guid threadId = await CreateThreadAsync(_author, "Gone");

        TestActor moderator = await _harness.SignInAsync(admin: true);
        using HttpClient moderatorClient = _harness.CreateClient(moderator);

        await moderatorClient.SendJsonAsync(
            HttpMethod.Post,
            $"{Moderation}/threads/{threadId}/status/Removed",
            new { Reason = "Off topic and abusive." });

        using HttpClient memberClient = _harness.CreateClient(_author);

        using HttpResponseMessage removed =
            await memberClient.GetAsync(new Uri($"{Base}/threads/{threadId}", UriKind.Relative));
        using HttpResponseMessage absent =
            await memberClient.GetAsync(new Uri($"{Base}/threads/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);

        // Byte-identical apart from the per-request trace identifier, so the difference
        // between "withdrawn" and "never existed" cannot be read off the response.
        Assert.Equal(
            WithoutTraceId(await removed.Content.ReadAsStringAsync()),
            WithoutTraceId(await absent.Content.ReadAsStringAsync()));
    }

    /// <summary>Strips the per-request trace identifier so two responses can be compared.</summary>
    private static string WithoutTraceId(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        using JsonDocument document = JsonDocument.Parse(body);

        return string.Join(
            '|',
            document.RootElement.EnumerateObject()
                .Where(property => !string.Equals(property.Name, "traceId", StringComparison.Ordinal))
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => $"{property.Name}={property.Value.GetRawText()}"));
    }

    [Fact]
    public async Task ReportsFlowThroughTheirLifecycleAndCannotBeReopened()
    {
        Guid threadId = await CreateThreadAsync(_author, "Queue me");
        Guid postId = await FirstPostIdAsync(_author, threadId);

        TestActor reporter = await SignedUpMemberAsync("queue-reporter");
        using HttpClient reporterClient = _harness.CreateClient(reporter);
        await reporterClient.SendJsonAsync(HttpMethod.Post, $"{Base}/reports", Report(postId));

        TestActor moderator = await _harness.SignInAsync(admin: true);
        using HttpClient client = _harness.CreateClient(moderator);

        using JsonDocument queue = await client.GetJsonAsync($"{Moderation}/reports?status=Open");
        JsonElement report = queue.RootElement.GetProperty("items")[0];
        Guid reportId = report.GetProperty("id").GetGuid();
        string rowVersion = report.GetProperty("rowVersion").GetString()!;

        using JsonDocument resolved = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Moderation}/reports/{reportId}/status/Resolved",
            new { Reason = "Post removed.", RowVersion = rowVersion },
            HttpStatusCode.OK);

        Assert.Equal("Resolved", resolved.RootElement.GetProperty("status").GetString());

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Moderation}/reports/{reportId}/status/Open",
            new
            {
                Reason = "Reopening.",
                RowVersion = resolved.RootElement.GetProperty("rowVersion").GetString(),
            },
            HttpStatusCode.BadRequest);

        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("status", out _));
    }

    // ---------------------------------------------------------------- helpers

    private static object NewThread(string title) => new
    {
        CategorySlug = "general",
        Title = title,
        Body = "An opening post with enough text to be a real post.",
    };

    private static object Report(Guid postId) => new
    {
        TargetType = "Post",
        TargetId = postId,
        ReasonCode = "Harassment",
        Detail = "Targeted at another member.",
    };

    private async Task<TestActor> SignedUpMemberAsync(string handle)
    {
        TestActor actor = await _harness.SignInAsync();
        using HttpClient client = _harness.CreateClient(actor);

        await client.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/me/community/profile",
            new { Handle = handle, Bio = (string?)null, AcceptGuidelines = true, AttestEligibility = true },
            HttpStatusCode.OK);

        return actor;
    }

    private async Task<Guid> CreateThreadAsync(TestActor actor, string title)
    {
        using HttpClient client = _harness.CreateClient(actor);

        using JsonDocument thread = await client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/threads", NewThread(title), HttpStatusCode.OK);

        return thread.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> FirstPostIdAsync(TestActor actor, Guid threadId)
    {
        using HttpClient client = _harness.CreateClient(actor);
        using JsonDocument thread = await client.GetJsonAsync($"{Base}/threads/{threadId}");

        return thread.RootElement.GetProperty("posts").GetProperty("items")[0]
            .GetProperty("id").GetGuid();
    }

    private async Task<string> FirstPostRowVersionAsync(TestActor actor, Guid threadId)
    {
        using HttpClient client = _harness.CreateClient(actor);
        using JsonDocument thread = await client.GetJsonAsync($"{Base}/threads/{threadId}");

        return thread.RootElement.GetProperty("posts").GetProperty("items")[0]
            .GetProperty("rowVersion").GetString()!;
    }

    /// <summary>Ensures the category the tests post into exists in every seed profile.</summary>
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
