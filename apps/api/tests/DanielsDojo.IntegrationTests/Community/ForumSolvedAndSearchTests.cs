using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Community;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Community;

/// <summary>
/// The accepted answer and discussion search.
/// </summary>
/// <remarks>
/// The rules under test: only the thread author chooses the answer, the answer must be a
/// still-published reply in that thread — never the opening post — and search matches titles
/// and published bodies while excerpting nothing that a reader could not read in place.
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class ForumSolvedAndSearchTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private const string Base = "/api/v1/community";

    private ApiHarness _harness = null!;
    private TestActor _author = null!;
    private TestActor _helper = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await SeedCategoryAsync();

        _harness = ApiHarness.Create(fixture);
        _author = await SignedUpMemberAsync("question-asker");
        _helper = await SignedUpMemberAsync("helpful-member");
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ---------------------------------------------------------------- accepted answer

    [Fact]
    public async Task TheAuthorMarksAReplyAsTheAnswerAndCanUnmarkIt()
    {
        Guid threadId = await CreateThreadAsync(_author, "How do I mix skin tones?");
        Guid replyId = await ReplyAsync(_helper, threadId, "Start from a warm mid value.");

        using HttpClient author = _harness.CreateClient(_author);

        using (JsonDocument solved = await author.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/threads/{threadId}/solved",
            new { PostId = replyId },
            HttpStatusCode.OK))
        {
            Assert.Equal(replyId, solved.RootElement.GetProperty("solvedPostId").GetGuid());
            Assert.True(solved.RootElement.GetProperty("canMarkSolved").GetBoolean());
        }

        // The listing carries the solved flag too.
        using (JsonDocument listing = await author.GetJsonAsync(
            $"{Base}/categories/general/threads"))
        {
            JsonElement row = listing.RootElement.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == threadId);
            Assert.True(row.GetProperty("isSolved").GetBoolean());
        }

        // Clearing is always allowed: an accepted answer is a signpost, not a lock.
        using JsonDocument cleared = await author.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/threads/{threadId}/solved",
            new { PostId = (Guid?)null },
            HttpStatusCode.OK);

        Assert.Equal(
            JsonValueKind.Null, cleared.RootElement.GetProperty("solvedPostId").ValueKind);
    }

    [Fact]
    public async Task OnlyTheThreadAuthorMayChooseTheAnswer()
    {
        Guid threadId = await CreateThreadAsync(_author, "Whose thread is this?");
        Guid replyId = await ReplyAsync(_helper, threadId, "Not mine to mark.");

        // The helper wrote the reply, but the question is not theirs. They are told the
        // thread is missing rather than which threads they don't own.
        using HttpClient helper = _harness.CreateClient(_helper);
        using HttpResponseMessage refused = await helper.SendJsonAsync(
            HttpMethod.Put, $"{Base}/threads/{threadId}/solved", new { PostId = replyId });

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);

        // For readers other than the author, the detail says so.
        using JsonDocument view = await helper.GetJsonAsync($"{Base}/threads/{threadId}");
        Assert.False(view.RootElement.GetProperty("canMarkSolved").GetBoolean());
    }

    [Fact]
    public async Task TheOpeningPostAndRemovedRepliesAreRefusedAsAnswers()
    {
        Guid threadId = await CreateThreadAsync(_author, "The question itself");
        Guid openingPostId = await FirstPostIdAsync(_author, threadId);

        using HttpClient author = _harness.CreateClient(_author);

        // The opening post is the question, not an answer.
        await author.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/threads/{threadId}/solved",
            new { PostId = openingPostId },
            HttpStatusCode.BadRequest);

        // A tombstoned reply cannot be held up as the answer either.
        Guid replyId = await ReplyAsync(_helper, threadId, "I retract this.");

        using (HttpClient helper = _harness.CreateClient(_helper))
        {
            using HttpResponseMessage _ = await helper.DeleteAsync(
                new Uri($"{Base}/posts/{replyId}", UriKind.Relative));
        }

        await author.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/threads/{threadId}/solved",
            new { PostId = replyId },
            HttpStatusCode.BadRequest);

        // And a reply from a different thread is rejected outright.
        Guid otherThreadId = await CreateThreadAsync(_author, "A different question");
        Guid foreignReplyId = await ReplyAsync(_helper, otherThreadId, "Answering elsewhere.");

        await author.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/threads/{threadId}/solved",
            new { PostId = foreignReplyId },
            HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- search

    [Fact]
    public async Task SearchFindsTitlesAndBodiesButNeverTombstonedText()
    {
        Guid byTitle = await CreateThreadAsync(_author, "Glazing over acrylics");
        Guid byBody = await CreateThreadAsync(_author, "A vague title");
        Guid replyId = await ReplyAsync(_helper, byBody, "Try glazing with a soft brush.");

        using HttpClient client = _harness.CreateClient(_author);

        using (JsonDocument results = await client.GetJsonAsync($"{Base}/search?q=glazing"))
        {
            List<JsonElement> items = [.. results.RootElement.GetProperty("items").EnumerateArray()];

            Assert.Equal(2, results.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Contains(items, item => item.GetProperty("threadId").GetGuid() == byTitle);

            JsonElement bodyHit = items.Single(
                item => item.GetProperty("threadId").GetGuid() == byBody);
            Assert.Contains("glazing", bodyHit.GetProperty("snippet").GetString()!,
                StringComparison.OrdinalIgnoreCase);
        }

        // Tombstoning the reply removes the only match in that thread.
        using (HttpClient helper = _harness.CreateClient(_helper))
        {
            using HttpResponseMessage _ = await helper.DeleteAsync(
                new Uri($"{Base}/posts/{replyId}", UriKind.Relative));
        }

        using JsonDocument after = await client.GetJsonAsync($"{Base}/search?q=glazing");
        List<JsonElement> remaining = [.. after.RootElement.GetProperty("items").EnumerateArray()];

        Assert.DoesNotContain(
            remaining, item => item.GetProperty("threadId").GetGuid() == byBody);
    }

    [Fact]
    public async Task SearchTreatsWildcardsAsLiteralsAndRefusesTinyQueries()
    {
        await CreateThreadAsync(_author, "Percent signs 100% of the time");

        using HttpClient client = _harness.CreateClient(_author);

        // '%' matches only literally: a lone wildcard would otherwise match everything.
        using (JsonDocument literal = await client.GetJsonAsync($"{Base}/search?q=100%25"))
        {
            Assert.Equal(1, literal.RootElement.GetProperty("totalCount").GetInt32());
        }

        using JsonDocument tooShort = await client.SendJsonAsync(
            HttpMethod.Get, $"{Base}/search?q=a", null, HttpStatusCode.BadRequest);
        Assert.NotNull(tooShort.ProblemCode());
    }

    // ---------------------------------------------------------------- helpers

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
            HttpMethod.Post,
            $"{Base}/threads",
            new
            {
                CategorySlug = "general",
                Title = title,
                Body = "An opening post with enough text to be a real post.",
            },
            HttpStatusCode.OK);

        return thread.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> ReplyAsync(TestActor actor, Guid threadId, string body)
    {
        using HttpClient client = _harness.CreateClient(actor);

        using JsonDocument thread = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/threads/{threadId}/posts",
            new { Body = body, ReplyToPostId = (Guid?)null },
            HttpStatusCode.OK);

        return thread.RootElement.GetProperty("posts").GetProperty("items")
            .EnumerateArray()
            .Last()
            .GetProperty("id").GetGuid();
    }

    private async Task<Guid> FirstPostIdAsync(TestActor actor, Guid threadId)
    {
        using HttpClient client = _harness.CreateClient(actor);
        using JsonDocument thread = await client.GetJsonAsync($"{Base}/threads/{threadId}");

        return thread.RootElement.GetProperty("posts").GetProperty("items")[0]
            .GetProperty("id").GetGuid();
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
