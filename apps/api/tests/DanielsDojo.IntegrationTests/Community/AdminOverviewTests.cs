using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Community;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Community;

/// <summary>
/// Covers the operator landing summary, forum category management, and the recent-threads
/// feed the community landing page reads.
/// </summary>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class AdminOverviewTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private const string Moderation = "/api/v1/admin/community";
    private const string Community = "/api/v1/community";

    private ApiHarness _harness = null!;
    private TestActor _admin = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _harness = ApiHarness.Create(fixture);
        _admin = await _harness.SignInAsync(admin: true);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ---------------------------------------------------------------- overview

    [Fact]
    public async Task OverviewCountsRealWorkAndCarriesNoContent()
    {
        using HttpClient client = _harness.CreateClient(_admin);
        await SetupAsync(_admin, "overview-admin");

        // Measured against the reference seed rather than an absolute number, so the test
        // asserts the effect of this arrangement instead of the seed's contents.
        using JsonDocument baseline = await client.GetJsonAsync($"{Moderation}/overview");
        int draftsBefore = baseline.RootElement.GetProperty("draftCourses").GetInt32();
        int readyBefore = baseline.RootElement.GetProperty("coursesReadyToPublish").GetInt32();

        // A draft that meets the publish prerequisites, and one that does not.
        Guid ready = await CourseAsync(client, "ready-course", publishOutline: true);
        await CourseAsync(client, "bare-course", publishOutline: false);

        const string Secret = "A-BODY-THAT-MUST-NOT-REACH-THE-OVERVIEW";
        Guid categoryId = await SeedCategoryAsync();
        Guid postId = await ThreadAsync(client, Secret);

        await Expect(
            client,
            HttpMethod.Post,
            $"{Moderation}/posts/{postId}/remove",
            new { Reason = "Removed for the overview test." },
            HttpStatusCode.NoContent);

        using JsonDocument overview = await client.GetJsonAsync($"{Moderation}/overview");
        JsonElement root = overview.RootElement;

        Assert.Equal(draftsBefore + 2, root.GetProperty("draftCourses").GetInt32());
        Assert.Equal(readyBefore + 1, root.GetProperty("coursesReadyToPublish").GetInt32());
        Assert.Equal(0, root.GetProperty("publishedCourses").GetInt32());
        Assert.True(root.GetProperty("forumCategories").GetInt32() >= 1);

        // The activity strip carries action, target, actor, and reason — and nothing else.
        JsonElement activity = root.GetProperty("recentActivity");
        Assert.NotEmpty(activity.EnumerateArray());

        string body = root.GetRawText();
        Assert.Contains("Community.Post.RemovedByModerator", body, StringComparison.Ordinal);
        Assert.Contains("Removed for the overview test.", body, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        foreach (string email in await context.Users.Select(user => user.Email).ToListAsync())
        {
            Assert.DoesNotContain(email, body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.NotEqual(default, categoryId);
    }

    [Fact]
    public async Task OverviewIsAdminOnly()
    {
        TestActor student = await _harness.SignInAsync();
        using HttpClient client = _harness.CreateClient(student);

        using HttpResponseMessage response =
            await client.GetAsync(new Uri($"{Moderation}/overview", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------------------------------------------------------------- categories

    [Fact]
    public async Task ArchivingACategoryHidesItFromMembersAndKeepsItsThreads()
    {
        using HttpClient client = _harness.CreateClient(_admin);
        await SetupAsync(_admin, "category-admin");

        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Moderation}/categories",
            new
            {
                Slug = "seasonal-topics",
                Name = "Seasonal topics",
                Description = "Things that matter for a while.",
                SortOrder = 9,
            },
            HttpStatusCode.OK);

        Guid categoryId = created.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("Active", created.RootElement.GetProperty("status").GetString());

        // A member can see it and post in it while it is active.
        using JsonDocument visible = await client.GetJsonAsync($"{Community}/categories");
        Assert.Contains(
            "seasonal-topics",
            visible.RootElement.EnumerateArray().Select(c => c.GetProperty("slug").GetString()),
            StringComparer.Ordinal);

        using JsonDocument thread = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Community}/threads",
            new { CategorySlug = "seasonal-topics", Title = "In season", Body = "A thread body." },
            HttpStatusCode.OK);

        Guid threadId = thread.RootElement.GetProperty("id").GetGuid();

        // Archiving needs a reason and is audited.
        using JsonDocument noReason = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Moderation}/categories/{categoryId}/status/Archived",
            new { Reason = "  ", RowVersion = created.RootElement.GetProperty("rowVersion").GetString() },
            HttpStatusCode.BadRequest);

        Assert.True(noReason.RootElement.GetProperty("errors").TryGetProperty("reason", out _));

        using JsonDocument archived = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Moderation}/categories/{categoryId}/status/Archived",
            new
            {
                Reason = "Out of season.",
                RowVersion = created.RootElement.GetProperty("rowVersion").GetString(),
            },
            HttpStatusCode.OK);

        Assert.Equal("Archived", archived.RootElement.GetProperty("status").GetString());

        // Members no longer see it, and it no longer accepts new threads.
        using JsonDocument afterwards = await client.GetJsonAsync($"{Community}/categories");
        Assert.DoesNotContain(
            "seasonal-topics",
            afterwards.RootElement.EnumerateArray().Select(c => c.GetProperty("slug").GetString()),
            StringComparer.Ordinal);

        using JsonDocument refused = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Community}/threads",
            new { CategorySlug = "seasonal-topics", Title = "Too late", Body = "A thread body." },
            HttpStatusCode.BadRequest);

        Assert.True(refused.RootElement.GetProperty("errors").TryGetProperty("categorySlug", out _));

        // The thread that was already there survives untouched.
        await using DanielsDojoDbContext context = fixture.CreateContext();
        ForumThread stored = await context.ForumThreads.SingleAsync(entry => entry.Id == threadId);
        Assert.Equal(ForumThreadStatus.Open, stored.Status);

        Assert.True(await context.AuditLogs.AnyAsync(
            entry => entry.Action == "Community.Category.StatusChanged"
                && entry.Reason == "Out of season."));
    }

    [Fact]
    public async Task ADuplicateCategorySlugIsRefused()
    {
        using HttpClient client = _harness.CreateClient(_admin);

        object payload = new
        {
            Slug = "duplicate-area",
            Name = "Duplicate area",
            Description = "First one wins.",
            SortOrder = 1,
        };

        await client.SendJsonAsync(HttpMethod.Post, $"{Moderation}/categories", payload, HttpStatusCode.OK);

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post, $"{Moderation}/categories", payload, HttpStatusCode.BadRequest);

        Assert.Equal("platform.duplicate_value", problem.ProblemCode());
    }

    [Fact]
    public async Task CategoryManagementIsAdminOnly()
    {
        TestActor student = await _harness.SignInAsync();
        using HttpClient client = _harness.CreateClient(student);

        using HttpResponseMessage list =
            await client.GetAsync(new Uri($"{Moderation}/categories", UriKind.Relative));
        using HttpResponseMessage create = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Moderation}/categories",
            new { Slug = "mine-now", Name = "Mine", Description = "Nope.", SortOrder = 0 });

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    // ---------------------------------------------------------------- recent threads

    [Fact]
    public async Task RecentThreadsSpanCategoriesAndRespectBlocks()
    {
        await SeedCategoryAsync();
        await SetupAsync(_admin, "recent-admin");

        using HttpClient adminClient = _harness.CreateClient(_admin);
        await ThreadAsync(adminClient, "The admin's thread body.");

        TestActor reader = await _harness.SignInAsync();
        await SetupAsync(reader, "recent-reader");
        using HttpClient readerClient = _harness.CreateClient(reader);

        using JsonDocument before = await readerClient.GetJsonAsync($"{Community}/threads/recent");
        Assert.NotEmpty(before.RootElement.EnumerateArray());
        Assert.Contains(
            "recent-admin",
            before.RootElement.EnumerateArray().Select(t => t.GetProperty("authorHandle").GetString()),
            StringComparer.Ordinal);

        // After a block, the same feed shows the thread without naming its author.
        await Expect(
            readerClient,
            HttpMethod.Post,
            $"{Community}/blocks",
            new { Handle = "recent-admin", ReasonCategory = "Personal" },
            HttpStatusCode.NoContent);

        using JsonDocument after = await readerClient.GetJsonAsync($"{Community}/threads/recent");
        string body = after.RootElement.GetRawText();

        Assert.DoesNotContain("recent-admin", body, StringComparison.Ordinal);
        Assert.Contains("Hidden member", body, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private static async Task Expect(
        HttpClient client,
        HttpMethod method,
        string path,
        object? payload,
        HttpStatusCode expected)
    {
        using HttpResponseMessage response = await client.SendJsonAsync(method, path, payload);
        string body = await response.Content.ReadAsStringAsync();

        Assert.True(
            expected == response.StatusCode,
            $"{method} {path} expected {expected} but returned {response.StatusCode}: {body}");
    }

    private async Task SetupAsync(TestActor actor, string handle)
    {
        using HttpClient client = _harness.CreateClient(actor);

        using JsonDocument _ = await client.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/me/community/profile",
            new { Handle = handle, Bio = (string?)null, AcceptGuidelines = true, AttestEligibility = true },
            HttpStatusCode.OK);
    }

    /// <summary>Creates a course, optionally with a published section and lesson beneath it.</summary>
    private static async Task<Guid> CourseAsync(HttpClient client, string slug, bool publishOutline)
    {
        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/admin/catalog/courses",
            new
            {
                Slug = slug,
                Title = slug,
                Summary = "A summary long enough to be useful.",
                Description = "A description that explains the course.",
                Level = "AllLevels",
                IncludedInMembership = false,
            },
            HttpStatusCode.Created);

        Guid courseId = created.RootElement.GetProperty("id").GetGuid();

        if (!publishOutline)
        {
            return courseId;
        }

        using JsonDocument withSection = await client.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/catalog/courses/{courseId}/sections",
            new { Title = "Section", Description = (string?)null },
            HttpStatusCode.OK);

        Guid sectionId = withSection.RootElement.GetProperty("sections")[0].GetProperty("id").GetGuid();

        using JsonDocument withLesson = await client.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/catalog/courses/{courseId}/sections/{sectionId}/lessons",
            new
            {
                Slug = "a-lesson",
                Title = "A lesson",
                Summary = (string?)null,
                LessonType = "Article",
                BodyMarkdown = "Body text.",
                IsPreview = false,
                EstimatedDurationSeconds = (int?)null,
            },
            HttpStatusCode.OK);

        JsonElement lesson = withLesson.RootElement.GetProperty("sections")[0].GetProperty("lessons")[0];

        using JsonDocument publishedLesson = await client.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/catalog/courses/{courseId}/lessons/{lesson.GetProperty("id").GetGuid()}/status/Published",
            new { Reason = "Ready.", RowVersion = lesson.GetProperty("rowVersion").GetString() },
            HttpStatusCode.OK);

        JsonElement section = publishedLesson.RootElement.GetProperty("sections")[0];

        using JsonDocument _ = await client.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/catalog/courses/{courseId}/sections/{sectionId}/status/Published",
            new { Reason = "Ready.", RowVersion = section.GetProperty("rowVersion").GetString() },
            HttpStatusCode.OK);

        return courseId;
    }

    private static async Task<Guid> ThreadAsync(HttpClient client, string body)
    {
        using JsonDocument thread = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Community}/threads",
            new { CategorySlug = "general", Title = "Overview thread", Body = body },
            HttpStatusCode.OK);

        return thread.RootElement.GetProperty("posts").GetProperty("items")[0].GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedCategoryAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        ForumCategory? existing = await context.ForumCategories
            .FirstOrDefaultAsync(category => category.Slug == "general");

        if (existing is not null)
        {
            return existing.Id;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var created = new ForumCategory
        {
            Id = Guid.NewGuid(),
            Slug = "general",
            Name = "General",
            Description = "Anything about the platform.",
            SortOrder = 0,
            Status = ForumCategoryStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.ForumCategories.Add(created);
        await context.SaveChangesAsync();

        return created.Id;
    }
}
