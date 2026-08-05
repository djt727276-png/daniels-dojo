using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Catalog;
using DanielsDojo.IntegrationTests.Commerce;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Learning;

/// <summary>
/// The learner experience end to end: curriculum, lesson navigation, progress, resume,
/// completion, and My Learning.
/// </summary>
/// <remarks>
/// The cases that matter here are the ones where a client could lie or a stale tab could
/// destroy something: a viewer asking for a lesson they have not bought, a preview viewer
/// asking for the materials, and an out-of-date position report arriving after real progress.
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class LearningExperienceTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private ApiHarness _harness = null!;
    private TestActor _member = null!;
    private TestActor _outsider = null!;
    private Guid _courseId;
    private Guid _firstLessonId;
    private Guid _secondLessonId;
    private Guid _previewLessonId;
    private Guid _draftLessonId;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        _harness = ApiHarness.Create(fixture);
        _member = await _harness.SignInAsync();
        _outsider = await _harness.SignInAsync();

        await SeedAsync();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ---------------------------------------------------------------- curriculum

    [Fact]
    public async Task AMemberSeesTheOutlineWithEveryPublishedLessonOpen()
    {
        using HttpClient member = _harness.CreateClient(_member);

        using JsonDocument curriculum = await member.GetJsonAsync("/api/v1/learning/courses/learning-course");
        JsonElement root = curriculum.RootElement;

        Assert.True(root.GetProperty("accessGranted").GetBoolean());
        Assert.Equal("Membership", root.GetProperty("accessReason").GetString());
        Assert.Equal(3, root.GetProperty("totalLessons").GetInt32());
        Assert.Equal(0, root.GetProperty("completedLessons").GetInt32());

        JsonElement[] lessons = [.. root.GetProperty("sections")
            .EnumerateArray()
            .SelectMany(section => section.GetProperty("lessons").EnumerateArray())];

        Assert.All(lessons, lesson => Assert.True(lesson.GetProperty("isAccessible").GetBoolean()));

        // A draft lesson is nobody's business, even inside a course they own.
        Assert.DoesNotContain(lessons, lesson => lesson.GetProperty("id").GetGuid() == _draftLessonId);

        // Resume starts at the very beginning when nothing has been touched.
        Assert.Equal(_firstLessonId, root.GetProperty("resumeLessonId").GetGuid());
    }

    [Fact]
    public async Task SomebodyWithoutAccessSeesTheOutlineButNotTheContent()
    {
        using HttpClient outsider = _harness.CreateClient(_outsider);

        using JsonDocument curriculum = await outsider.GetJsonAsync("/api/v1/learning/courses/learning-course");
        JsonElement root = curriculum.RootElement;

        // The shape of the course is a selling point; the material is not.
        Assert.False(root.GetProperty("accessGranted").GetBoolean());
        Assert.True(root.GetProperty("isPreviewOnly").GetBoolean());
        Assert.Equal(3, root.GetProperty("totalLessons").GetInt32());

        JsonElement[] lessons = [.. root.GetProperty("sections")
            .EnumerateArray()
            .SelectMany(section => section.GetProperty("lessons").EnumerateArray())];

        Assert.Single(lessons, lesson => lesson.GetProperty("isAccessible").GetBoolean());
    }

    [Fact]
    public async Task AnAnonymousViewerGetsThePreviewOutlineAndNothingMore()
    {
        using HttpClient anonymous = _harness.Factory.CreateClient();

        using JsonDocument curriculum = await anonymous.GetJsonAsync("/api/v1/learning/courses/learning-course");

        Assert.True(curriculum.RootElement.GetProperty("isPreviewOnly").GetBoolean());

        using HttpResponseMessage locked = await anonymous.GetAsync(
            new Uri($"/api/v1/learning/lessons/{_firstLessonId}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, locked.StatusCode);
    }

    [Fact]
    public async Task AnUnpublishedCourseIsNotFoundRatherThanForbidden()
    {
        using HttpClient member = _harness.CreateClient(_member);

        using HttpResponseMessage response = await member.GetAsync(
            new Uri("/api/v1/learning/courses/hidden-course", UriKind.Relative));

        // Reporting 403 would confirm the course exists to anyone who guessed the slug.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------- lessons

    [Fact]
    public async Task OpeningALessonGivesItsNeighboursForNavigation()
    {
        using HttpClient member = _harness.CreateClient(_member);

        using JsonDocument lesson = await member.GetJsonAsync($"/api/v1/learning/lessons/{_secondLessonId}");
        JsonElement root = lesson.RootElement;

        Assert.Equal(_firstLessonId, root.GetProperty("previousLessonId").GetGuid());
        Assert.Equal(_previewLessonId, root.GetProperty("nextLessonId").GetGuid());
        Assert.Equal("learning-course", root.GetProperty("courseSlug").GetString());
    }

    [Fact]
    public async Task APreviewViewerGetsTheLessonButNotItsDownloads()
    {
        using HttpClient outsider = _harness.CreateClient(_outsider);

        using JsonDocument preview = await outsider.GetJsonAsync(
            $"/api/v1/learning/lessons/{_previewLessonId}");

        Assert.Equal("PublicPreview", preview.RootElement.GetProperty("accessReason").GetString());
        Assert.Empty(preview.RootElement.GetProperty("resources").EnumerateArray());

        // The same lesson, for somebody who actually holds the course, does carry them.
        using HttpClient member = _harness.CreateClient(_member);
        using JsonDocument owned = await member.GetJsonAsync(
            $"/api/v1/learning/lessons/{_previewLessonId}");

        Assert.Single(owned.RootElement.GetProperty("resources").EnumerateArray());
    }

    [Fact]
    public async Task ALessonInsideACourseSomebodyDoesNotHoldIsRefused()
    {
        using HttpClient outsider = _harness.CreateClient(_outsider);

        using JsonDocument problem = await outsider.SendJsonAsync(
            HttpMethod.Get,
            $"/api/v1/learning/lessons/{_firstLessonId}",
            null,
            HttpStatusCode.Forbidden);

        Assert.Equal("access.denied.purchaserequired", problem.ProblemCode());
    }

    // ---------------------------------------------------------------- progress

    [Fact]
    public async Task ProgressAdvancesResumeAndCompletionFinishesTheCourse()
    {
        using HttpClient member = _harness.CreateClient(_member);

        await Report(member, _firstLessonId, 120, completed: true);

        using JsonDocument afterFirst = await member.GetJsonAsync(
            "/api/v1/learning/courses/learning-course");

        Assert.Equal(1, afterFirst.RootElement.GetProperty("completedLessons").GetInt32());
        Assert.Equal(_secondLessonId, afterFirst.RootElement.GetProperty("resumeLessonId").GetGuid());

        await Report(member, _secondLessonId, 30, completed: true);

        using JsonDocument last = await Report(member, _previewLessonId, 10, completed: true);

        Assert.True(last.RootElement.GetProperty("courseCompleted").GetBoolean());
        Assert.Equal(3, last.RootElement.GetProperty("completedLessons").GetInt32());
    }

    [Fact]
    public async Task AStaleReportCannotRewindOrUncompleteAnything()
    {
        using HttpClient member = _harness.CreateClient(_member);

        await Report(member, _firstLessonId, 600, completed: true);

        // A tab left open since before the learner watched on properly.
        using JsonDocument stale = await Report(member, _firstLessonId, 5, completed: false);

        Assert.Equal(600, stale.RootElement.GetProperty("lastPositionSeconds").GetInt32());
        Assert.NotEqual(
            JsonValueKind.Null,
            stale.RootElement.GetProperty("completedAtUtc").ValueKind);
    }

    [Fact]
    public async Task ANegativePositionIsRejected()
    {
        using HttpClient member = _harness.CreateClient(_member);

        using JsonDocument problem = await member.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/learning/lessons/{_firstLessonId}/progress",
            new { positionSeconds = -1, completed = false },
            HttpStatusCode.BadRequest);

        Assert.Equal("platform.validation_failed", problem.ProblemCode());
    }

    [Fact]
    public async Task SomebodyWithoutAccessCannotRecordProgress()
    {
        using HttpClient outsider = _harness.CreateClient(_outsider);

        using HttpResponseMessage response = await outsider.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/learning/lessons/{_firstLessonId}/progress",
            new { positionSeconds = 10, completed = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Empty(await context.LessonProgress.Where(p => p.UserId == _outsider.UserId).ToListAsync());
    }

    [Fact]
    public async Task APreviewViewerWatchesButLeavesNoProgressBehind()
    {
        using HttpClient outsider = _harness.CreateClient(_outsider);

        using HttpResponseMessage response = await outsider.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/learning/lessons/{_previewLessonId}/progress",
            new { positionSeconds = 10, completed = true });

        // Progress belongs to people who hold the course, not to browsers passing through.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ProgressRequiresASignedInLearner()
    {
        using HttpClient anonymous = _harness.Factory.CreateClient();

        using HttpResponseMessage response = await anonymous.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/learning/lessons/{_firstLessonId}/progress",
            new { positionSeconds = 10, completed = false });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------------------------------------------------------- my learning

    [Fact]
    public async Task MyLearningShowsTheCourseWithRealCompletion()
    {
        using HttpClient member = _harness.CreateClient(_member);

        await Report(member, _firstLessonId, 60, completed: true);

        using JsonDocument shelf = await member.GetJsonAsync("/api/v1/learning/my-learning");
        JsonElement course = shelf.RootElement.EnumerateArray().Single();

        Assert.Equal("learning-course", course.GetProperty("slug").GetString());
        Assert.Equal(3, course.GetProperty("totalLessons").GetInt32());
        Assert.Equal(1, course.GetProperty("completedLessons").GetInt32());
        Assert.Equal(33, course.GetProperty("percentComplete").GetInt32());
        Assert.Equal(_secondLessonId, course.GetProperty("resumeLessonId").GetGuid());
        Assert.Equal("Membership", course.GetProperty("accessReason").GetString());
    }

    [Fact]
    public async Task MyLearningIsEmptyForSomebodyWhoHoldsNothing()
    {
        using HttpClient outsider = _harness.CreateClient(_outsider);

        using JsonDocument shelf = await outsider.GetJsonAsync("/api/v1/learning/my-learning");

        Assert.Empty(shelf.RootElement.EnumerateArray());
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<JsonDocument> Report(
        HttpClient client,
        Guid lessonId,
        int positionSeconds,
        bool completed) =>
        await client.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/learning/lessons/{lessonId}/progress",
            new { positionSeconds, completed },
            HttpStatusCode.OK);

    private async Task SeedAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        Course course = CatalogFactory.Course(
            "learning-course", "Learning course", PublicationStatus.Published, true, now);
        Course hidden = CatalogFactory.Course(
            "hidden-course", "Hidden course", PublicationStatus.Draft, true, now);

        CourseSection section = CatalogFactory.Section(
            course.Id, "Section one", 0, PublicationStatus.Published, now);
        CourseSection draftSection = CatalogFactory.Section(
            course.Id, "Not ready", 1, PublicationStatus.Draft, now);

        Lesson first = CatalogFactory.Lesson(
            course.Id, section.Id, "first", 0,
            PublicationStatus.Published, LessonType.Article, false, "First body.", now);
        Lesson second = CatalogFactory.Lesson(
            course.Id, section.Id, "second", 1,
            PublicationStatus.Published, LessonType.Video, false, null, now);
        Lesson preview = CatalogFactory.Lesson(
            course.Id, section.Id, "preview", 2,
            PublicationStatus.Published, LessonType.Article, true, "Preview body.", now);

        // Published lesson inside a draft section: publication does not cascade upward, so this
        // must stay invisible to learners.
        Lesson draft = CatalogFactory.Lesson(
            course.Id, draftSection.Id, "draft", 0,
            PublicationStatus.Published, LessonType.Article, false, "Draft body.", now);

        context.Courses.AddRange(course, hidden);
        context.CourseSections.AddRange(section, draftSection);
        context.Lessons.AddRange(first, second, preview, draft);

        context.LessonResources.Add(new LessonResource
        {
            Id = Guid.NewGuid(),
            LessonId = preview.Id,
            DisplayName = "Worksheet",

            // A published resource must name the object it serves; the schema refuses a
            // download link that points at nothing.
            BlobObjectName = $"courses/{course.Id:N}/resources/worksheet.pdf",
            MediaType = "application/pdf",
            SizeBytes = 2048,
            SortOrder = 0,
            Status = PublicationStatus.Published,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        OfferPrice membership = CommerceFactory.MembershipOffer(
            context, $"membership-{Guid.NewGuid():N}", now);

        Guid subscriptionId = CommerceFactory.Subscription(
            context, _member.UserId, membership, now.AddDays(-1), now.AddMonths(1),
            SubscriptionStatus.Active);

        context.Entitlements.Add(new Entitlement
        {
            Id = Guid.CreateVersion7(),
            UserId = _member.UserId,
            Scope = EntitlementScope.AllMembershipCourses,
            Source = EntitlementSource.Subscription,
            SubscriptionId = subscriptionId,
            Status = EntitlementStatus.Active,
            StartsAtUtc = now.AddDays(-1),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        await context.SaveChangesAsync();

        _courseId = course.Id;
        _firstLessonId = first.Id;
        _secondLessonId = second.Id;
        _previewLessonId = preview.Id;
        _draftLessonId = draft.Id;

        Assert.NotEqual(Guid.Empty, _courseId);
    }
}
