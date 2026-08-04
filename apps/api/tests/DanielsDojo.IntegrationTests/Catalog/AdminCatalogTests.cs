using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Auditing;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Catalog;

/// <summary>
/// Exercises catalog authoring end to end: the Admin gate, the status graph and its
/// prerequisites, optimistic concurrency, exact-set reordering, and the audit trail.
/// </summary>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class AdminCatalogTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private const string Base = "/api/v1/admin/catalog";

    private ApiHarness _harness = null!;
    private TestActor _admin = null!;

    public async Task InitializeAsync()
    {
        // The reference seed installs the roles that first sign-in assigns, so provisioning
        // works exactly as it does against a real environment.
        await fixture.ResetAsync();
        _harness = ApiHarness.Create(fixture);
        _admin = await _harness.SignInAsync(admin: true);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ---------------------------------------------------------------- authorization

    [Fact]
    public async Task AnonymousRequest_Is401()
    {
        using HttpClient client = _harness.Factory.CreateClient();

        using HttpResponseMessage response =
            await client.GetAsync(new Uri($"{Base}/courses", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Student_Is403()
    {
        TestActor student = await _harness.SignInAsync();
        using HttpClient client = _harness.CreateClient(student);

        using HttpResponseMessage response =
            await client.GetAsync(new Uri($"{Base}/courses", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Student_CannotCreateACourse()
    {
        TestActor student = await _harness.SignInAsync();
        using HttpClient client = _harness.CreateClient(student);

        using HttpResponseMessage response = await client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/courses", NewCourse("blocked-course"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------------------------------------------------------------- creation and validation

    [Fact]
    public async Task CreateCourse_StartsAsDraftAndIsInvisibleToThePublic()
    {
        using HttpClient client = AdminClient();

        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/courses", NewCourse("draft-only-course"), HttpStatusCode.Created);

        Assert.Equal("Draft", created.RootElement.GetProperty("status").GetString());
        Assert.False(created.RootElement.GetProperty("slugLocked").GetBoolean());
        Assert.True(created.RootElement.TryGetProperty("rowVersion", out JsonElement version));
        Assert.False(string.IsNullOrWhiteSpace(version.GetString()));

        // The Admin list spans every status; the public list does not.
        using JsonDocument adminList = await client.GetJsonAsync($"{Base}/courses");
        Assert.Contains("draft-only-course", Slugs(adminList), StringComparer.Ordinal);

        using HttpClient anonymous = _harness.Factory.CreateClient();
        using JsonDocument publicList = await anonymous.GetJsonAsync("/api/v1/catalog/courses");
        Assert.DoesNotContain("draft-only-course", Slugs(publicList), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("Not A Slug")]
    [InlineData("UPPERCASE")]
    [InlineData("trailing-")]
    [InlineData("do")]
    public async Task CreateCourse_RejectsAnInvalidSlug(string slug)
    {
        using HttpClient client = AdminClient();

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses",
            NewCourse(slug),
            HttpStatusCode.BadRequest);

        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("slug", out _));
    }

    [Fact]
    public async Task CreateCourse_RejectsADuplicateSlug()
    {
        using HttpClient client = AdminClient();
        await CreateCourseAsync(client, "duplicate-course");

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses",
            NewCourse("duplicate-course"),
            HttpStatusCode.BadRequest);

        Assert.Equal("platform.duplicate_value", problem.ProblemCode());
    }

    // ---------------------------------------------------------------- concurrency

    [Fact]
    public async Task UpdateCourse_WithAStaleRowVersion_Is409WithTheStableCode()
    {
        using HttpClient client = AdminClient();
        CourseHandle course = await CreateCourseAsync(client, "concurrent-course");

        // First write succeeds and invalidates the token the second writer still holds.
        using JsonDocument first = await client.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/courses/{course.Id}",
            UpdatedCourse(course.Slug, "First edit wins", course.RowVersion),
            HttpStatusCode.OK);

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/courses/{course.Id}",
            UpdatedCourse(course.Slug, "Second edit loses", course.RowVersion),
            HttpStatusCode.Conflict);

        Assert.Equal("platform.concurrency_conflict", problem.ProblemCode());

        // The losing write left nothing behind.
        using JsonDocument current = await client.GetJsonAsync($"{Base}/courses/{course.Id}");
        Assert.Equal("First edit wins", current.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task UpdateCourse_WithAMalformedRowVersion_IsRejected()
    {
        using HttpClient client = AdminClient();
        CourseHandle course = await CreateCourseAsync(client, "malformed-token-course");

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/courses/{course.Id}",
            UpdatedCourse(course.Slug, "Rejected", "not-base64!!"),
            HttpStatusCode.BadRequest);

        Assert.Equal("platform.invalid_row_version", problem.ProblemCode());
    }

    // ---------------------------------------------------------------- status graph

    [Fact]
    public async Task StatusChange_RequiresANonBlankReason()
    {
        using HttpClient client = AdminClient();
        CourseHandle course = await CreateCourseAsync(client, "reason-required-course");

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/status/Archived",
            new { Reason = "   ", course.RowVersion },
            HttpStatusCode.BadRequest);

        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("reason", out _));
    }

    [Fact]
    public async Task PublishingACourse_RequiresAPublishedSectionWithAPublishedLesson()
    {
        using HttpClient client = AdminClient();
        CourseHandle course = await CreateCourseAsync(client, "empty-course");

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/status/Published",
            new { Reason = "Ready to launch.", course.RowVersion },
            HttpStatusCode.BadRequest);

        Assert.Equal("catalog.publish_prerequisite", problem.ProblemCode());
    }

    [Fact]
    public async Task PublishingAnArticleLesson_RequiresABody()
    {
        using HttpClient client = AdminClient();
        CourseHandle course = await CreateCourseAsync(client, "bodyless-article-course");
        JsonDocument detail = await AddSectionAsync(client, course.Id, "Section one");
        Guid sectionId = FirstSectionId(detail);

        using JsonDocument withLesson = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/sections/{sectionId}/lessons",
            new
            {
                Slug = "bodyless-lesson",
                Title = "Bodyless lesson",
                Summary = (string?)null,
                LessonType = "Article",
                BodyMarkdown = (string?)null,
                IsPreview = false,
                EstimatedDurationSeconds = (int?)null,
            },
            HttpStatusCode.OK);

        LessonHandle lesson = FirstLesson(withLesson);
        detail.Dispose();

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/lessons/{lesson.Id}/status/Published",
            new { Reason = "Publishing.", lesson.RowVersion },
            HttpStatusCode.BadRequest);

        Assert.Equal("catalog.publish_prerequisite", problem.ProblemCode());
    }

    [Fact]
    public async Task PublishingAVideoLesson_RequiresAReadyVideo()
    {
        using HttpClient client = AdminClient();
        PublishedCourse published = await BuildPublishableCourseAsync(client, "video-course");

        using JsonDocument withLesson = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{published.CourseId}/sections/{published.SectionId}/lessons",
            new
            {
                Slug = "video-lesson",
                Title = "Video lesson",
                Summary = (string?)null,
                LessonType = "Video",
                BodyMarkdown = (string?)null,
                IsPreview = false,
                EstimatedDurationSeconds = 300,
            },
            HttpStatusCode.OK);

        LessonHandle videoLesson = LessonBySlug(withLesson, "video-lesson");

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{published.CourseId}/lessons/{videoLesson.Id}/status/Published",
            new { Reason = "Publishing.", videoLesson.RowVersion },
            HttpStatusCode.BadRequest);

        Assert.Equal("catalog.publish_prerequisite", problem.ProblemCode());

        // Give the lesson a Ready asset and the same command now succeeds.
        await using (DanielsDojoDbContext context = fixture.CreateContext())
        {
            context.LessonVideos.Add(new LessonVideo
            {
                Id = Guid.NewGuid(),
                LessonId = videoLesson.Id,
                Status = LessonVideoStatus.Ready,
                DurationSeconds = 300,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });

            await context.SaveChangesAsync();
        }

        using JsonDocument ok = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{published.CourseId}/lessons/{videoLesson.Id}/status/Published",
            new { Reason = "Publishing.", videoLesson.RowVersion },
            HttpStatusCode.OK);

        Assert.Equal("Published", LessonBySlug(ok, "video-lesson").Status);
    }

    [Fact]
    public async Task PublishingACourse_DoesNotCascadeToItsChildren()
    {
        using HttpClient client = AdminClient();
        PublishedCourse published = await BuildPublishableCourseAsync(client, "no-cascade-course");

        // A second, still-draft lesson exists alongside the published one.
        using JsonDocument withDraft = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{published.CourseId}/sections/{published.SectionId}/lessons",
            NewLesson("still-draft"),
            HttpStatusCode.OK);

        string rowVersion = withDraft.RootElement.GetProperty("rowVersion").GetString()!;

        using JsonDocument publishedCourse = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{published.CourseId}/status/Published",
            new { Reason = "Launch.", RowVersion = rowVersion },
            HttpStatusCode.OK);

        Assert.Equal("Published", publishedCourse.RootElement.GetProperty("status").GetString());
        Assert.Equal("Draft", LessonBySlug(publishedCourse, "still-draft").Status);
    }

    [Fact]
    public async Task ArchivedCourse_CanOnlyReturnToDraft()
    {
        using HttpClient client = AdminClient();
        CourseHandle course = await CreateCourseAsync(client, "archived-course");

        using JsonDocument archived = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/status/Archived",
            new { Reason = "Withdrawing.", course.RowVersion },
            HttpStatusCode.OK);

        string archivedVersion = archived.RootElement.GetProperty("rowVersion").GetString()!;

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/status/Published",
            new { Reason = "Straight back out.", RowVersion = archivedVersion },
            HttpStatusCode.BadRequest);

        Assert.Equal("catalog.invalid_transition", problem.ProblemCode());

        using JsonDocument draft = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/status/Draft",
            new { Reason = "Reworking.", RowVersion = archivedVersion },
            HttpStatusCode.OK);

        Assert.Equal("Draft", draft.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task RepublishingKeepsTheFirstPublicationDateAndLocksTheSlug()
    {
        using HttpClient client = AdminClient();
        PublishedCourse published = await BuildPublishableCourseAsync(client, "locked-slug-course");
        CourseHandle live = await PublishCourseAsync(client, published.CourseId);

        using JsonDocument afterFirstPublish = await client.GetJsonAsync($"{Base}/courses/{published.CourseId}");
        DateTimeOffset firstPublished =
            afterFirstPublish.RootElement.GetProperty("publishedAtUtc").GetDateTimeOffset();
        Assert.True(afterFirstPublish.RootElement.GetProperty("slugLocked").GetBoolean());

        // Renaming a published course is refused with its own code, not a generic failure.
        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/courses/{published.CourseId}",
            UpdatedCourse("a-different-slug", "Renamed", live.RowVersion),
            HttpStatusCode.BadRequest);

        Assert.Equal("catalog.slug_locked", problem.ProblemCode());

        // Withdraw and republish: the original publication date survives.
        using JsonDocument draft = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{published.CourseId}/status/Draft",
            new { Reason = "Temporary withdrawal.", live.RowVersion },
            HttpStatusCode.OK);

        using JsonDocument republished = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{published.CourseId}/status/Published",
            new { Reason = "Back online.", RowVersion = draft.RootElement.GetProperty("rowVersion").GetString() },
            HttpStatusCode.OK);

        Assert.Equal(
            firstPublished,
            republished.RootElement.GetProperty("publishedAtUtc").GetDateTimeOffset());
    }

    // ---------------------------------------------------------------- reordering

    [Fact]
    public async Task ReorderSections_RequiresTheExactSiblingSet()
    {
        using HttpClient client = AdminClient();
        CourseHandle course = await CreateCourseAsync(client, "reorder-course");

        using JsonDocument one = await AddSectionAsync(client, course.Id, "One");
        using JsonDocument two = await AddSectionAsync(client, course.Id, "Two");
        using JsonDocument three = await AddSectionAsync(client, course.Id, "Three");

        SectionHandle[] sections = Sections(three);
        Assert.Equal(3, sections.Length);

        // A partial list is refused rather than being applied to the items it names.
        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/sections/order",
            new { Items = new[] { new { sections[0].Id, sections[0].RowVersion } } },
            HttpStatusCode.BadRequest);

        Assert.Equal("catalog.reorder_mismatch", problem.ProblemCode());
    }

    [Fact]
    public async Task ReorderSections_ReversesTheOrderWithoutCollidingOnSortOrder()
    {
        using HttpClient client = AdminClient();
        CourseHandle course = await CreateCourseAsync(client, "reversible-course");

        using JsonDocument one = await AddSectionAsync(client, course.Id, "One");
        using JsonDocument two = await AddSectionAsync(client, course.Id, "Two");
        using JsonDocument three = await AddSectionAsync(client, course.Id, "Three");

        SectionHandle[] original = Sections(three);
        Assert.Equal(["One", "Two", "Three"], original.Select(section => section.Title).ToArray());

        using JsonDocument reordered = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/sections/order",
            new
            {
                Items = original
                    .Reverse()
                    .Select(section => new { section.Id, section.RowVersion })
                    .ToArray(),
            },
            HttpStatusCode.OK);

        SectionHandle[] after = Sections(reordered);
        Assert.Equal(["Three", "Two", "One"], after.Select(section => section.Title).ToArray());
        Assert.Equal([0, 1, 2], after.Select(section => section.SortOrder).ToArray());
    }

    [Fact]
    public async Task ReorderSections_WithAStaleRowVersion_Is409()
    {
        using HttpClient client = AdminClient();
        CourseHandle course = await CreateCourseAsync(client, "stale-reorder-course");

        using JsonDocument one = await AddSectionAsync(client, course.Id, "One");
        using JsonDocument two = await AddSectionAsync(client, course.Id, "Two");

        SectionHandle[] stale = Sections(two);

        // Rename a section so the tokens captured above no longer match the stored rows.
        using JsonDocument renamed = await client.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/courses/{course.Id}/sections/{stale[0].Id}",
            new { Title = "One renamed", Description = (string?)null, stale[0].RowVersion },
            HttpStatusCode.OK);

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/sections/order",
            new
            {
                Items = stale.Reverse().Select(section => new { section.Id, section.RowVersion }).ToArray(),
            },
            HttpStatusCode.Conflict);

        Assert.Equal("platform.concurrency_conflict", problem.ProblemCode());

        // The rejected reorder left the stored order untouched.
        using JsonDocument current = await client.GetJsonAsync($"{Base}/courses/{course.Id}");
        Assert.Equal(["One renamed", "Two"], Sections(current).Select(section => section.Title).ToArray());
    }

    [Fact]
    public async Task ReorderLessons_ReordersWithinASection()
    {
        using HttpClient client = AdminClient();
        CourseHandle course = await CreateCourseAsync(client, "lesson-order-course");
        using JsonDocument sectionDoc = await AddSectionAsync(client, course.Id, "Only section");
        Guid sectionId = FirstSectionId(sectionDoc);

        using JsonDocument first = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/sections/{sectionId}/lessons",
            NewLesson("alpha"),
            HttpStatusCode.OK);
        using JsonDocument second = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/sections/{sectionId}/lessons",
            NewLesson("beta"),
            HttpStatusCode.OK);

        LessonHandle[] lessons = Lessons(second);
        Assert.Equal(["alpha", "beta"], lessons.Select(lesson => lesson.Slug).ToArray());

        using JsonDocument reordered = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/sections/{sectionId}/lessons/order",
            new
            {
                Items = lessons.Reverse().Select(lesson => new { lesson.Id, lesson.RowVersion }).ToArray(),
            },
            HttpStatusCode.OK);

        Assert.Equal(["beta", "alpha"], Lessons(reordered).Select(lesson => lesson.Slug).ToArray());
    }

    // ---------------------------------------------------------------- audit

    [Fact]
    public async Task EveryStatusChange_WritesOneAuditRowCarryingTheReason()
    {
        using HttpClient client = AdminClient();
        CourseHandle course = await CreateCourseAsync(client, "audited-course");

        using JsonDocument archived = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/status/Archived",
            new { Reason = "Withdrawn pending a rewrite.", course.RowVersion },
            HttpStatusCode.OK);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        List<AuditLog> entries = await context.AuditLogs
            .Where(entry => entry.TargetId == course.Id.ToString("D"))
            .OrderBy(entry => entry.OccurredAtUtc)
            .ToListAsync();

        AuditLog statusEntry = Assert.Single(
            entries,
            entry => entry.Action == "Catalog.Course.StatusChanged");

        Assert.Equal("Withdrawn pending a rewrite.", statusEntry.Reason);
        Assert.Equal(_admin.UserId, statusEntry.ActorUserId);
        Assert.False(string.IsNullOrWhiteSpace(statusEntry.CorrelationId));
        Assert.Contains("Archived", statusEntry.MetadataJson, StringComparison.Ordinal);

        // Creation is audited too, so the trail starts before the first status decision.
        Assert.Contains(entries, entry => entry.Action == "Catalog.Course.Created");
    }

    [Fact]
    public async Task ARejectedWrite_LeavesNoAuditRow()
    {
        using HttpClient client = AdminClient();
        CourseHandle course = await CreateCourseAsync(client, "unaudited-failure-course");

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/status/Published",
            new { Reason = "Attempted launch.", course.RowVersion },
            HttpStatusCode.BadRequest);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        bool anyStatusEntry = await context.AuditLogs.AnyAsync(entry =>
            entry.TargetId == course.Id.ToString("D")
            && entry.Action == "Catalog.Course.StatusChanged");

        Assert.False(anyStatusEntry);
    }

    // ---------------------------------------------------------------- tags

    [Fact]
    public async Task Tags_AreCreatedOnceAndAttachedToACourse()
    {
        using HttpClient client = AdminClient();
        CourseHandle course = await CreateCourseAsync(client, "tagged-course");

        using JsonDocument tag = await client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/tags", new { Name = "Architecture" }, HttpStatusCode.OK);

        Guid tagId = tag.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("ARCHITECTURE", tag.RootElement.GetProperty("normalizedName").GetString());

        using JsonDocument duplicate = await client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/tags", new { Name = "architecture" }, HttpStatusCode.BadRequest);

        Assert.Equal("platform.duplicate_value", duplicate.ProblemCode());

        using JsonDocument tagged = await client.SendJsonAsync(
            HttpMethod.Put,
            $"{Base}/courses/{course.Id}/tags",
            new { TagIds = new[] { tagId }, course.RowVersion },
            HttpStatusCode.OK);

        Assert.Equal(
            "Architecture",
            tagged.RootElement.GetProperty("tags")[0].GetProperty("name").GetString());
    }

    // ---------------------------------------------------------------- helpers

    private HttpClient AdminClient() => _harness.CreateClient(_admin);

    private static object NewCourse(string slug) => new
    {
        Slug = slug,
        Title = $"Course {slug}",
        Summary = "A summary that is long enough to be useful.",
        Description = "A description that explains what the course covers.",
        Level = "AllLevels",
        IncludedInMembership = true,
    };

    private static object UpdatedCourse(string slug, string title, string rowVersion) => new
    {
        Slug = slug,
        Title = title,
        Summary = "A summary that is long enough to be useful.",
        Description = "A description that explains what the course covers.",
        Level = "Intermediate",
        IncludedInMembership = true,
        ImageAltText = (string?)null,
        RowVersion = rowVersion,
    };

    private static object NewLesson(string slug) => new
    {
        Slug = slug,
        Title = $"Lesson {slug}",
        Summary = (string?)null,
        LessonType = "Article",
        BodyMarkdown = "Body content for the lesson.",
        IsPreview = false,
        EstimatedDurationSeconds = 120,
    };

    private static async Task<CourseHandle> CreateCourseAsync(HttpClient client, string slug)
    {
        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/courses", NewCourse(slug), HttpStatusCode.Created);

        return new CourseHandle(
            created.RootElement.GetProperty("id").GetGuid(),
            slug,
            created.RootElement.GetProperty("rowVersion").GetString()!);
    }

    private static Task<JsonDocument> AddSectionAsync(HttpClient client, Guid courseId, string title) =>
        client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{courseId}/sections",
            new { Title = title, Description = (string?)null },
            HttpStatusCode.OK);

    /// <summary>
    /// Builds a course with one published section containing one published article lesson —
    /// the minimum state from which the course itself may be published.
    /// </summary>
    private static async Task<PublishedCourse> BuildPublishableCourseAsync(HttpClient client, string slug)
    {
        CourseHandle course = await CreateCourseAsync(client, slug);

        using JsonDocument withSection = await AddSectionAsync(client, course.Id, "Getting started");
        Guid sectionId = FirstSectionId(withSection);

        using JsonDocument withLesson = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/sections/{sectionId}/lessons",
            NewLesson("first-lesson"),
            HttpStatusCode.OK);

        LessonHandle lesson = LessonBySlug(withLesson, "first-lesson");

        using JsonDocument publishedLesson = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/lessons/{lesson.Id}/status/Published",
            new { Reason = "Content reviewed.", lesson.RowVersion },
            HttpStatusCode.OK);

        SectionHandle section = Sections(publishedLesson).Single(item => item.Id == sectionId);

        using JsonDocument publishedSection = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{course.Id}/sections/{sectionId}/status/Published",
            new { Reason = "Section reviewed.", section.RowVersion },
            HttpStatusCode.OK);

        return new PublishedCourse(course.Id, sectionId);
    }

    private static async Task<CourseHandle> PublishCourseAsync(HttpClient client, Guid courseId)
    {
        using JsonDocument current = await client.GetJsonAsync($"{Base}/courses/{courseId}");

        using JsonDocument published = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/courses/{courseId}/status/Published",
            new { Reason = "Launch.", RowVersion = current.RootElement.GetProperty("rowVersion").GetString() },
            HttpStatusCode.OK);

        return new CourseHandle(
            courseId,
            published.RootElement.GetProperty("slug").GetString()!,
            published.RootElement.GetProperty("rowVersion").GetString()!);
    }

    private static string[] Slugs(JsonDocument list) =>
        list.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("slug").GetString()!)
            .ToArray();

    private static Guid FirstSectionId(JsonDocument detail) => Sections(detail)[0].Id;

    private static SectionHandle[] Sections(JsonDocument detail) =>
        detail.RootElement.GetProperty("sections")
            .EnumerateArray()
            .Select(section => new SectionHandle(
                section.GetProperty("id").GetGuid(),
                section.GetProperty("title").GetString()!,
                section.GetProperty("sortOrder").GetInt32(),
                section.GetProperty("status").GetString()!,
                section.GetProperty("rowVersion").GetString()!))
            .ToArray();

    private static LessonHandle[] Lessons(JsonDocument detail) =>
        detail.RootElement.GetProperty("sections")
            .EnumerateArray()
            .SelectMany(section => section.GetProperty("lessons").EnumerateArray())
            .Select(lesson => new LessonHandle(
                lesson.GetProperty("id").GetGuid(),
                lesson.GetProperty("slug").GetString()!,
                lesson.GetProperty("status").GetString()!,
                lesson.GetProperty("rowVersion").GetString()!))
            .ToArray();

    private static LessonHandle FirstLesson(JsonDocument detail) => Lessons(detail)[0];

    private static LessonHandle LessonBySlug(JsonDocument detail, string slug) =>
        Lessons(detail).Single(lesson => string.Equals(lesson.Slug, slug, StringComparison.Ordinal));

    private sealed record CourseHandle(Guid Id, string Slug, string RowVersion);

    private sealed record SectionHandle(Guid Id, string Title, int SortOrder, string Status, string RowVersion);

    private sealed record LessonHandle(Guid Id, string Slug, string Status, string RowVersion);

    private sealed record PublishedCourse(Guid CourseId, Guid SectionId);
}
