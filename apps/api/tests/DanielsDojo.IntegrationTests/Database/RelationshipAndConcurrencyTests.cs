using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Database;

/// <summary>
/// Proves composite ownership, restrictive deletion of historical records, UTC persistence,
/// and rowversion concurrency behaviour.
/// </summary>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class RelationshipAndConcurrencyTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task LessonReferencingSectionFromAnotherCourse_IsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Course courseA = TestEntities.Course();
        Course courseB = TestEntities.Course();
        CourseSection sectionInB = TestEntities.Section(courseB.Id, 1);

        context.Courses.AddRange(courseA, courseB);
        context.CourseSections.Add(sectionInB);
        await context.SaveChangesAsync();

        // Course A, but a section owned by course B: the composite foreign key must refuse it.
        context.Lessons.Add(TestEntities.Lesson(courseA.Id, sectionInB.Id));

        DbUpdateException exception =
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains(
            "FK_Lessons_CourseSections_CourseId_CourseSectionId",
            exception.InnerException?.Message ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LessonReferencingSectionFromSameCourse_IsAccepted()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Course course = TestEntities.Course();
        CourseSection section = TestEntities.Section(course.Id, 1);
        context.Courses.Add(course);
        context.CourseSections.Add(section);
        context.Lessons.Add(TestEntities.Lesson(course.Id, section.Id));

        await context.SaveChangesAsync();

        Assert.Equal(1, await context.Lessons.CountAsync(lesson => lesson.CourseId == course.Id));
    }

    [Fact]
    public async Task DeletingUserWithAnOrder_IsRejectedByRestrictiveForeignKey()
    {
        Guid userId;

        await using (DanielsDojoDbContext setup = fixture.CreateContext())
        {
            CommerceGraph graph = await CommerceGraph.CreateAsync(setup);
            userId = graph.UserId;
            setup.Orders.Add(TestEntities.Order(graph.UserId));
            await setup.SaveChangesAsync();
        }

        // Purchase history must survive any attempt to remove the purchaser.
        await AssertDeleteRejectedAsync<User>(userId);
    }

    [Fact]
    public async Task DeletingCourseWithLessons_IsRejectedByRestrictiveForeignKey()
    {
        Guid courseId;

        await using (DanielsDojoDbContext setup = fixture.CreateContext())
        {
            LearningGraph graph = await LearningGraph.CreateAsync(setup);
            courseId = graph.CourseId;
        }

        await AssertDeleteRejectedAsync<Course>(courseId);
    }

    [Fact]
    public async Task DeletingUserWithAnEntitlement_IsRejectedByRestrictiveForeignKey()
    {
        Guid userId;

        await using (DanielsDojoDbContext setup = fixture.CreateContext())
        {
            CommerceGraph graph = await CommerceGraph.CreateAsync(setup);
            userId = graph.UserId;

            Subscription subscription =
                TestEntities.Subscription(graph.UserId, graph.MembershipOfferId, graph.PriceId);
            setup.Subscriptions.Add(subscription);
            setup.Entitlements.Add(TestEntities.MembershipEntitlement(graph.UserId, subscription.Id));
            await setup.SaveChangesAsync();
        }

        await AssertDeleteRejectedAsync<User>(userId);
    }

    /// <summary>
    /// Deletes through a context that has not loaded the dependents, so the DELETE actually
    /// reaches SQL Server and the restrictive foreign key — rather than EF's change tracker —
    /// is what rejects it.
    /// </summary>
    private async Task AssertDeleteRejectedAsync<TEntity>(Guid id)
        where TEntity : class
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        TEntity entity = await context.Set<TEntity>().FindAsync(id)
            ?? throw new InvalidOperationException($"Expected a {typeof(TEntity).Name} with id {id}.");

        context.Set<TEntity>().Remove(entity);

        DbUpdateException exception =
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains(
            "REFERENCE constraint",
            exception.InnerException?.Message ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonUtcTimestamp_IsPersistedAtOffsetZero()
    {
        Guid courseId;

        await using (DanielsDojoDbContext writeContext = fixture.CreateContext())
        {
            Course course = TestEntities.Course();
            courseId = course.Id;

            // Deliberately a non-zero offset: the context must normalise it before saving.
            course.CreatedAtUtc = new DateTimeOffset(2026, 8, 3, 9, 30, 0, TimeSpan.FromHours(-5));
            course.UpdatedAtUtc = new DateTimeOffset(2026, 8, 3, 9, 30, 0, TimeSpan.FromHours(-5));
            course.PublishedAtUtc = new DateTimeOffset(2026, 8, 3, 9, 30, 0, TimeSpan.FromHours(5.5));

            writeContext.Courses.Add(course);
            await writeContext.SaveChangesAsync();
        }

        await using DanielsDojoDbContext readContext = fixture.CreateContext();
        Course stored = await readContext.Courses.SingleAsync(c => c.Id == courseId);

        Assert.Equal(TimeSpan.Zero, stored.CreatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, stored.UpdatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, stored.PublishedAtUtc!.Value.Offset);

        // Normalisation must convert the instant, not merely relabel the offset.
        Assert.Equal(
            new DateTimeOffset(2026, 8, 3, 14, 30, 0, TimeSpan.Zero),
            stored.CreatedAtUtc);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 3, 4, 0, 0, TimeSpan.Zero),
            stored.PublishedAtUtc!.Value);
    }

    [Fact]
    public async Task StaleRowVersionUpdate_ThrowsDbUpdateConcurrencyException()
    {
        Guid courseId;

        await using (DanielsDojoDbContext setup = fixture.CreateContext())
        {
            Course course = TestEntities.Course();
            courseId = course.Id;
            setup.Courses.Add(course);
            await setup.SaveChangesAsync();
        }

        await using DanielsDojoDbContext firstReader = fixture.CreateContext();
        await using DanielsDojoDbContext secondReader = fixture.CreateContext();

        Course firstCopy = await firstReader.Courses.SingleAsync(c => c.Id == courseId);
        Course secondCopy = await secondReader.Courses.SingleAsync(c => c.Id == courseId);

        // First writer wins and bumps the rowversion.
        firstCopy.Title = "Updated by the first writer";
        await firstReader.SaveChangesAsync();

        // Second writer still holds the original rowversion.
        secondCopy.Title = "Updated by the second writer";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondReader.SaveChangesAsync());
    }

    [Fact]
    public async Task RowVersionIsAssignedAndChangesOnUpdate()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Course course = TestEntities.Course();
        context.Courses.Add(course);
        await context.SaveChangesAsync();

        byte[] afterInsert = course.RowVersion;
        Assert.NotEmpty(afterInsert);

        course.Title = "Changed";
        await context.SaveChangesAsync();

        Assert.NotEqual(afterInsert, course.RowVersion);
    }

    [Fact]
    public async Task EntitlementSurvivesRevocationWithoutBeingDeleted()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        CommerceGraph graph = await CommerceGraph.CreateAsync(context);

        Subscription subscription =
            TestEntities.Subscription(graph.UserId, graph.MembershipOfferId, graph.PriceId);
        Entitlement entitlement = TestEntities.MembershipEntitlement(graph.UserId, subscription.Id);
        context.Subscriptions.Add(subscription);
        context.Entitlements.Add(entitlement);
        await context.SaveChangesAsync();

        entitlement.Status = EntitlementStatus.Revoked;
        entitlement.RevokedAtUtc = DateTimeOffset.UtcNow;
        entitlement.RevocationReason = "Refund issued after access review.";
        await context.SaveChangesAsync();

        // Revocation is a status change, never a delete: the audit trail stays intact.
        Entitlement stored = await context.Entitlements.SingleAsync(e => e.Id == entitlement.Id);
        Assert.Equal(EntitlementStatus.Revoked, stored.Status);
        Assert.NotNull(stored.RevokedAtUtc);
        Assert.Equal(1, await context.Entitlements.CountAsync());
    }
}
