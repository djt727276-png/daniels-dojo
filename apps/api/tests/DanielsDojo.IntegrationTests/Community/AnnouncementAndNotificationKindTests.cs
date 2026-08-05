using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Domain.Community;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Catalog;
using DanielsDojo.IntegrationTests.Commerce;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Community;

/// <summary>
/// Course announcements and the platform notification kinds.
/// </summary>
/// <remarks>
/// An announcement is an ordinary pinned forum thread plus a pointer fanned out to enrolled
/// members — content lives in the forum where every rule already applies. Purchase and
/// completion notifications are written in the same transaction as the order and certificate
/// they announce.
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class AnnouncementAndNotificationKindTests(SqlServerDatabaseFixture fixture)
    : IAsyncLifetime
{
    private ApiHarness _harness = null!;
    private TestActor _admin = null!;
    private TestActor _enrolled = null!;
    private TestActor _outsider = null!;
    private Guid _courseId;
    private Guid _lessonId;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        _harness = ApiHarness.Create(fixture);
        _admin = await _harness.SignInAsync(admin: true);
        _enrolled = await _harness.SignInAsync();
        _outsider = await _harness.SignInAsync();

        await SeedAsync();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task AnAnnouncementBecomesAPinnedThreadAndOnlyEnrolledMembersHearAboutIt()
    {
        using HttpClient admin = _harness.CreateClient(_admin);

        Guid threadId;

        using (JsonDocument posted = await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/community/courses/{_courseId}/announcements",
            new { Title = "New section published", Body = "Three new lessons on edge control." },
            HttpStatusCode.OK))
        {
            threadId = posted.RootElement.GetProperty("threadId").GetGuid();
            Assert.Equal(1, posted.RootElement.GetProperty("membersNotified").GetInt32());
        }

        await using DanielsDojoDbContext context = fixture.CreateContext();

        ForumThread thread = await context.ForumThreads.SingleAsync(
            candidate => candidate.Id == threadId);
        Assert.True(thread.IsPinned);
        Assert.Equal(_courseId, thread.CourseId);

        // The reserved category was created exactly once.
        Assert.Equal(1, await context.ForumCategories.CountAsync(
            category => category.Slug == "announcements"));

        // The enrolled member has the pointer; the outsider heard nothing.
        Assert.True(await context.Notifications.AnyAsync(
            notification => notification.RecipientUserId == _enrolled.UserId
                && notification.Kind == NotificationKind.CourseAnnouncement
                && notification.TargetId == threadId));
        Assert.False(await context.Notifications.AnyAsync(
            notification => notification.RecipientUserId == _outsider.UserId));
    }

    [Fact]
    public async Task AStudentCannotPostAnnouncements()
    {
        using HttpClient student = _harness.CreateClient(_enrolled);

        using HttpResponseMessage refused = await student.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/community/courses/{_courseId}/announcements",
            new { Title = "Fake", Body = "Fake." });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task FinishingACourseWritesACompletionNotificationWithTheCertificate()
    {
        using HttpClient member = _harness.CreateClient(_enrolled);

        using (JsonDocument _ = await member.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/learning/lessons/{_lessonId}/progress",
            new { positionSeconds = 0, completed = true },
            HttpStatusCode.OK))
        {
        }

        await using DanielsDojoDbContext context = fixture.CreateContext();

        Guid certificateId = (await context.Certificates.SingleAsync(
            certificate => certificate.UserId == _enrolled.UserId)).Id;

        Assert.True(await context.Notifications.AnyAsync(
            notification => notification.RecipientUserId == _enrolled.UserId
                && notification.Kind == NotificationKind.CourseCompleted
                && notification.TargetId == certificateId));
    }

    // ---------------------------------------------------------------- seeding

    private async Task SeedAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        Course course = CatalogFactory.Course(
            "announce-course", "Announce course", PublicationStatus.Published, true, now);
        CourseSection section = CatalogFactory.Section(
            course.Id, "Section", 0, PublicationStatus.Published, now);
        Lesson lesson = CatalogFactory.Lesson(
            course.Id, section.Id, "only-lesson", 0,
            PublicationStatus.Published, LessonType.Article, false, "Body.", now);

        context.Courses.Add(course);
        context.CourseSections.Add(section);
        context.Lessons.Add(lesson);

        OfferPrice membership = CommerceFactory.MembershipOffer(
            context, $"membership-{Guid.NewGuid():N}", now);
        Guid subscriptionId = CommerceFactory.Subscription(
            context, _enrolled.UserId, membership, now.AddDays(-1), now.AddMonths(1),
            SubscriptionStatus.Active);

        context.Entitlements.Add(new Entitlement
        {
            Id = Guid.CreateVersion7(),
            UserId = _enrolled.UserId,
            Scope = EntitlementScope.AllMembershipCourses,
            Source = EntitlementSource.Subscription,
            SubscriptionId = subscriptionId,
            Status = EntitlementStatus.Active,
            StartsAtUtc = now.AddDays(-1),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        context.Enrollments.Add(new DanielsDojo.Domain.Learning.Enrollment
        {
            Id = Guid.CreateVersion7(),
            UserId = _enrolled.UserId,
            CourseId = course.Id,
            EnrolledAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        await context.SaveChangesAsync();

        _courseId = course.Id;
        _lessonId = lesson.Id;
    }
}
