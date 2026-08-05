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
/// Course reviews.
/// </summary>
/// <remarks>
/// The rules under test: only an entitled member with real progress may review, one slot per
/// member per course, the aggregate counts published rows only, and moderation hides with a
/// reason rather than deleting.
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class CourseReviewTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private ApiHarness _harness = null!;
    private TestActor _member = null!;
    private TestActor _outsider = null!;
    private TestActor _admin = null!;
    private Guid _courseId;
    private Guid _lessonId;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        _harness = ApiHarness.Create(fixture);
        _member = await _harness.SignInAsync();
        _outsider = await _harness.SignInAsync();
        _admin = await _harness.SignInAsync(admin: true);

        await SeedAsync();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task OnlyAnEntitledMemberWithProgressMayReview()
    {
        using HttpClient outsider = _harness.CreateClient(_outsider);

        // No entitlement at all.
        using (JsonDocument refused = await outsider.SendJsonAsync(
            HttpMethod.Put,
            $"/api/v1/learning/courses/review-course/review",
            new { rating = 5, body = "Great!" },
            HttpStatusCode.Forbidden))
        {
            Assert.Equal("reviews.not_entitled", refused.ProblemCode());
        }

        using HttpClient member = _harness.CreateClient(_member);

        // Entitled, but no lesson completed yet: the progress threshold refuses.
        using (JsonDocument early = await member.SendJsonAsync(
            HttpMethod.Put,
            $"/api/v1/learning/courses/review-course/review",
            new { rating = 5, body = "Great!" },
            HttpStatusCode.Forbidden))
        {
            Assert.Equal("reviews.progress_required", early.ProblemCode());
        }

        await CompleteLessonAsync(member);

        using JsonDocument written = await member.SendJsonAsync(
            HttpMethod.Put,
            $"/api/v1/learning/courses/review-course/review",
            new { rating = 4, body = "Practical from the first lesson." },
            HttpStatusCode.OK);

        Assert.True(written.RootElement.GetProperty("isMine").GetBoolean());
    }

    [Fact]
    public async Task EditingReplacesTheSameSlotAndMarksItEdited()
    {
        using HttpClient member = _harness.CreateClient(_member);
        await CompleteLessonAsync(member);

        await Write(member, 5, "First impression.");
        await Write(member, 3, "Settled opinion.");

        await using DanielsDojoDbContext context = fixture.CreateContext();

        var mine = await context.CourseReviews
            .Where(review => review.UserId == _member.UserId)
            .Select(review => new { review.Rating, review.EditedAtUtc })
            .SingleAsync();

        Assert.Equal(3, mine.Rating);
        Assert.NotNull(mine.EditedAtUtc);
    }

    [Fact]
    public async Task TheAggregateCountsPublishedReviewsOnlyAndModerationCorrectsIt()
    {
        using HttpClient member = _harness.CreateClient(_member);
        await CompleteLessonAsync(member);
        await Write(member, 5, "Excellent.");

        using (JsonDocument before = await ReadReviews())
        {
            Assert.Equal(5.0, before.RootElement.GetProperty("averageRating").GetDouble());
            Assert.Equal(1, before.RootElement.GetProperty("reviewCount").GetInt32());
        }

        Guid reviewId;

        await using (DanielsDojoDbContext context = fixture.CreateContext())
        {
            reviewId = (await context.CourseReviews.SingleAsync()).Id;
        }

        using HttpClient admin = _harness.CreateClient(_admin);

        // No reason, no hide.
        await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/reviews/{reviewId}/hide",
            new { reason = " " },
            HttpStatusCode.BadRequest);

        await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/reviews/{reviewId}/hide",
            new { reason = "Contains a discount code." },
            HttpStatusCode.OK);

        using (JsonDocument after = await ReadReviews())
        {
            Assert.Equal(JsonValueKind.Null, after.RootElement.GetProperty("averageRating").ValueKind);
            Assert.Equal(0, after.RootElement.GetProperty("reviewCount").GetInt32());
        }

        // The author cannot edit around the moderator.
        using (JsonDocument locked = await member.SendJsonAsync(
            HttpMethod.Put,
            $"/api/v1/learning/courses/review-course/review",
            new { rating = 5, body = "Trying again." },
            HttpStatusCode.Conflict))
        {
            Assert.NotNull(locked.ProblemCode());
        }

        // Restore brings it back to the aggregate.
        await admin.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/admin/reviews/{reviewId}/restore", null, HttpStatusCode.OK);

        using JsonDocument restored = await ReadReviews();
        Assert.Equal(1, restored.RootElement.GetProperty("reviewCount").GetInt32());
    }

    [Fact]
    public async Task AStudentCannotReachModeration()
    {
        using HttpClient member = _harness.CreateClient(_member);

        using HttpResponseMessage list = await member.GetAsync(
            new Uri("/api/v1/admin/reviews", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        using HttpResponseMessage hide = await member.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/reviews/{Guid.NewGuid()}/hide",
            new { reason = "Nope." });
        Assert.Equal(HttpStatusCode.Forbidden, hide.StatusCode);
    }

    [Fact]
    public async Task DeletingOwnReviewLeavesATombstoneAndTheAggregateDropsIt()
    {
        using HttpClient member = _harness.CreateClient(_member);
        await CompleteLessonAsync(member);
        await Write(member, 4, "Good.");

        using HttpResponseMessage deleted = await member.DeleteAsync(
            new Uri($"/api/v1/learning/courses/review-course/review", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        using JsonDocument after = await ReadReviews();
        Assert.Equal(0, after.RootElement.GetProperty("reviewCount").GetInt32());

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Equal(1, await context.CourseReviews.CountAsync());
    }

    // ---------------------------------------------------------------- helpers

    private async Task<JsonDocument> ReadReviews()
    {
        using HttpClient anonymous = _harness.Factory.CreateClient();
        return await anonymous.GetJsonAsync("/api/v1/catalog/courses/review-course/reviews");
    }

    private static async Task Write(HttpClient client, int rating, string body) =>
        (await client.SendJsonAsync(
            HttpMethod.Put,
            $"/api/v1/learning/courses/review-course/review",
            new { rating, body },
            HttpStatusCode.OK)).Dispose();

    private async Task CompleteLessonAsync(HttpClient client) =>
        (await client.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/learning/lessons/{_lessonId}/progress",
            new { positionSeconds = 0, completed = true },
            HttpStatusCode.OK)).Dispose();

    private async Task SeedAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        Course course = CatalogFactory.Course(
            "review-course", "Review course", PublicationStatus.Published, true, now);
        CourseSection section = CatalogFactory.Section(
            course.Id, "Section", 0, PublicationStatus.Published, now);
        Lesson lesson = CatalogFactory.Lesson(
            course.Id, section.Id, "one", 0,
            PublicationStatus.Published, LessonType.Article, false, "Body.", now);

        context.Courses.Add(course);
        context.CourseSections.Add(section);
        context.Lessons.Add(lesson);

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
        _lessonId = lesson.Id;
    }
}
