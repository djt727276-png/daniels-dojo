using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Domain.Learning;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Database;

/// <summary>
/// Proves every uniqueness rule is enforced by the database itself, so a race or a retried
/// webhook cannot create a duplicate that application code merely intended to prevent.
/// </summary>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class UniquenessConstraintTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DuplicateExternalIssuerAndSubject_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        User first = TestEntities.User(subject: "shared-subject");
        context.Users.Add(first);
        await context.SaveChangesAsync();

        User duplicate = TestEntities.User(subject: "shared-subject");
        context.Users.Add(duplicate);

        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateNormalizedEmail_IsAllowed_BecauseEmailIsNotTheOwnershipKey()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        context.Users.Add(TestEntities.User(email: "shared@example.test"));
        context.Users.Add(TestEntities.User(email: "shared@example.test"));

        // Two provider identities may legitimately present the same address.
        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Users.CountAsync(user => user.NormalizedEmail == "SHARED@EXAMPLE.TEST"));
    }

    [Fact]
    public async Task DuplicateRoleNormalizedName_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        context.Roles.Add(TestEntities.Role("Auditor"));
        await context.SaveChangesAsync();

        context.Roles.Add(TestEntities.Role("Auditor"));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateCourseSlug_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        context.Courses.Add(TestEntities.Course("duplicate-slug"));
        await context.SaveChangesAsync();

        context.Courses.Add(TestEntities.Course("duplicate-slug"));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateLessonSlugWithinSameCourse_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Course course = TestEntities.Course();
        CourseSection section = TestEntities.Section(course.Id, 1);
        context.Courses.Add(course);
        context.CourseSections.Add(section);
        context.Lessons.Add(TestEntities.Lesson(course.Id, section.Id, "intro", sortOrder: 1));
        await context.SaveChangesAsync();

        context.Lessons.Add(TestEntities.Lesson(course.Id, section.Id, "intro", sortOrder: 2));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task SameLessonSlugInDifferentCourses_IsAllowed()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Course first = TestEntities.Course();
        Course second = TestEntities.Course();
        CourseSection firstSection = TestEntities.Section(first.Id, 1);
        CourseSection secondSection = TestEntities.Section(second.Id, 1);

        context.Courses.AddRange(first, second);
        context.CourseSections.AddRange(firstSection, secondSection);
        context.Lessons.Add(TestEntities.Lesson(first.Id, firstSection.Id, "intro"));
        context.Lessons.Add(TestEntities.Lesson(second.Id, secondSection.Id, "intro"));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Lessons.CountAsync(lesson => lesson.Slug == "intro"));
    }

    [Fact]
    public async Task DuplicateSectionSortOrderWithinCourse_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Course course = TestEntities.Course();
        context.Courses.Add(course);
        context.CourseSections.Add(TestEntities.Section(course.Id, 1));
        await context.SaveChangesAsync();

        context.CourseSections.Add(TestEntities.Section(course.Id, 1));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateLessonSortOrderWithinSection_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Course course = TestEntities.Course();
        CourseSection section = TestEntities.Section(course.Id, 1);
        context.Courses.Add(course);
        context.CourseSections.Add(section);
        context.Lessons.Add(TestEntities.Lesson(course.Id, section.Id, sortOrder: 1));
        await context.SaveChangesAsync();

        context.Lessons.Add(TestEntities.Lesson(course.Id, section.Id, sortOrder: 1));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateMuxAssetId_IsRejected_ButManyNullsAreAllowed()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Course course = TestEntities.Course();
        CourseSection section = TestEntities.Section(course.Id, 1);
        Lesson first = TestEntities.Lesson(course.Id, section.Id, sortOrder: 1);
        Lesson second = TestEntities.Lesson(course.Id, section.Id, sortOrder: 2);
        Lesson third = TestEntities.Lesson(course.Id, section.Id, sortOrder: 3);

        context.Courses.Add(course);
        context.CourseSections.Add(section);
        context.Lessons.AddRange(first, second, third);

        // The filter means unset provider identifiers never collide.
        context.LessonVideos.Add(TestEntities.LessonVideo(first.Id));
        context.LessonVideos.Add(TestEntities.LessonVideo(second.Id));
        await context.SaveChangesAsync();

        context.LessonVideos.Add(TestEntities.LessonVideo(third.Id, assetId: "asset_shared"));
        await context.SaveChangesAsync();

        LessonVideo existing = await context.LessonVideos.SingleAsync(video => video.LessonId == first.Id);
        existing.MuxAssetId = "asset_shared";

        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateBlobObjectName_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Course course = TestEntities.Course();
        CourseSection section = TestEntities.Section(course.Id, 1);
        Lesson lesson = TestEntities.Lesson(course.Id, section.Id);

        context.Courses.Add(course);
        context.CourseSections.Add(section);
        context.Lessons.Add(lesson);
        context.LessonResources.Add(TestEntities.LessonResource(lesson.Id, "handouts/shared.pdf"));
        await context.SaveChangesAsync();

        context.LessonResources.Add(TestEntities.LessonResource(lesson.Id, "handouts/shared.pdf"));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateWebhookEventId_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        context.WebhookEvents.Add(TestEntities.WebhookEvent("evt_duplicate"));
        await context.SaveChangesAsync();

        context.WebhookEvents.Add(TestEntities.WebhookEvent("evt_duplicate"));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateStripeSubscriptionId_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        context.Subscriptions.Add(
            TestEntities.Subscription(graph.UserId, graph.MembershipOfferId, graph.PriceId, "sub_duplicate"));
        await context.SaveChangesAsync();

        context.Subscriptions.Add(
            TestEntities.Subscription(graph.UserId, graph.MembershipOfferId, graph.PriceId, "sub_duplicate"));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task MultipleHistoricalSubscriptionsForSameUserAndOffer_AreAllowed()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Subscription canceled =
            TestEntities.Subscription(graph.UserId, graph.MembershipOfferId, graph.PriceId);
        canceled.Status = SubscriptionStatus.Canceled;
        Subscription active =
            TestEntities.Subscription(graph.UserId, graph.MembershipOfferId, graph.PriceId);

        context.Subscriptions.AddRange(canceled, active);

        // Subscription history must never be squashed by a uniqueness rule.
        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Subscriptions.CountAsync(s => s.UserId == graph.UserId));
    }

    [Fact]
    public async Task SecondEntitlementForSameSubscription_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Subscription subscription =
            TestEntities.Subscription(graph.UserId, graph.MembershipOfferId, graph.PriceId);
        context.Subscriptions.Add(subscription);
        context.Entitlements.Add(TestEntities.MembershipEntitlement(graph.UserId, subscription.Id));
        await context.SaveChangesAsync();

        // A redelivered webhook must not be able to mint a second grant.
        context.Entitlements.Add(TestEntities.MembershipEntitlement(graph.UserId, subscription.Id));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task SecondEntitlementForSameOrderItem_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Order order = TestEntities.Order(graph.UserId);
        OrderItem item = TestEntities.OrderItem(
            order.Id, graph.CourseOfferId, graph.PriceId, graph.CourseId);
        context.Orders.Add(order);
        context.OrderItems.Add(item);
        context.Entitlements.Add(
            TestEntities.PurchaseEntitlement(graph.UserId, graph.CourseId, item.Id));
        await context.SaveChangesAsync();

        context.Entitlements.Add(
            TestEntities.PurchaseEntitlement(graph.UserId, graph.CourseId, item.Id));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateOrderItemForSameOffer_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Order order = TestEntities.Order(graph.UserId);
        context.Orders.Add(order);
        context.OrderItems.Add(
            TestEntities.OrderItem(order.Id, graph.CourseOfferId, graph.PriceId, graph.CourseId));
        await context.SaveChangesAsync();

        context.OrderItems.Add(
            TestEntities.OrderItem(order.Id, graph.CourseOfferId, graph.PriceId, graph.CourseId));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateEnrollment_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        User user = TestEntities.User();
        Course course = TestEntities.Course();
        context.Users.Add(user);
        context.Courses.Add(course);
        context.Enrollments.Add(TestEntities.Enrollment(user.Id, course.Id));
        await context.SaveChangesAsync();

        context.Enrollments.Add(TestEntities.Enrollment(user.Id, course.Id));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateLessonProgress_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        User user = TestEntities.User();
        Course course = TestEntities.Course();
        CourseSection section = TestEntities.Section(course.Id, 1);
        Lesson lesson = TestEntities.Lesson(course.Id, section.Id);

        context.Users.Add(user);
        context.Courses.Add(course);
        context.CourseSections.Add(section);
        context.Lessons.Add(lesson);
        context.LessonProgress.Add(TestEntities.LessonProgress(user.Id, lesson.Id));
        await context.SaveChangesAsync();

        context.LessonProgress.Add(TestEntities.LessonProgress(user.Id, lesson.Id));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateOfferCode_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        context.Offers.Add(TestEntities.MembershipOffer("shared-code"));
        await context.SaveChangesAsync();

        context.Offers.Add(TestEntities.MembershipOffer("shared-code"));
        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateStripeCustomerId_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        User first = TestEntities.User();
        User second = TestEntities.User();
        context.Users.AddRange(first, second);
        context.StripeCustomers.Add(new StripeCustomer
        {
            Id = Guid.NewGuid(),
            UserId = first.Id,
            StripeCustomerId = "cus_shared",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();

        context.StripeCustomers.Add(new StripeCustomer
        {
            Id = Guid.NewGuid(),
            UserId = second.Id,
            StripeCustomerId = "cus_shared",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        await AssertUniqueViolationAsync(context);
    }

    [Fact]
    public async Task DuplicateRefundAndDisputeExternalIds_AreRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Order order = TestEntities.Order(graph.UserId);
        context.Orders.Add(order);
        context.Refunds.Add(TestEntities.OrderRefund(order.Id, "re_shared"));
        context.PaymentDisputes.Add(TestEntities.OrderDispute(order.Id, "dp_shared"));
        await context.SaveChangesAsync();

        context.Refunds.Add(TestEntities.OrderRefund(order.Id, "re_shared"));
        await AssertUniqueViolationAsync(context);

        await using DanielsDojoDbContext disputeContext = fixture.CreateContext();
        disputeContext.PaymentDisputes.Add(TestEntities.OrderDispute(order.Id, "dp_shared"));
        await AssertUniqueViolationAsync(disputeContext);
    }

    private static async Task AssertUniqueViolationAsync(DanielsDojoDbContext context)
    {
        DbUpdateException exception =
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains(
            "duplicate key",
            exception.InnerException?.Message ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }
}
