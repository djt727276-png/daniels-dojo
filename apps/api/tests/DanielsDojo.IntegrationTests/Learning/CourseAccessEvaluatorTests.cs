using DanielsDojo.Application.Learning;
using DanielsDojo.Application.System;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Infrastructure.Learning;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.Infrastructure.Persistence.Seeding;
using DanielsDojo.IntegrationTests.Catalog;
using DanielsDojo.IntegrationTests.Commerce;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Learning;

/// <summary>
/// The full access matrix against a real database.
/// </summary>
/// <remarks>
/// This is the test that stops access from quietly collapsing into a boolean. Every row below
/// is a rule somebody could plausibly get wrong: a membership covering a course it does not
/// cover, a cancelled membership cutting off a lifetime purchase, a revoked lifetime taking a
/// membership with it, or a Development convenience surviving into a deployed environment.
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class CourseAccessEvaluatorTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    private Guid _membershipCourseId;
    private Guid _standaloneCourseId;
    private Guid _draftCourseId;
    private Guid _publishedLessonId;
    private Guid _previewLessonId;
    private Guid _draftLessonId;
    private Guid _standaloneLessonId;

    public async Task InitializeAsync()
    {
        await fixture.ResetWithoutSeedAsync();
        await SeedRolesAsync();
        await SeedCatalogAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------- anonymous

    [Fact]
    public async Task AnonymousGetsPreviewsAndNothingElse()
    {
        CourseAccessEvaluator evaluator = Evaluator();

        CourseAccess preview = await evaluator.EvaluateLessonAsync(null, _previewLessonId);
        Assert.True(preview.Granted);
        Assert.Equal(CourseAccessReason.PublicPreview, preview.Reason);
        Assert.True(preview.IsPreviewOnly);

        // A preview shows what the course is like; it does not hand out the materials.
        Assert.False(preview.AllowsResourceDownload);

        CourseAccess locked = await evaluator.EvaluateLessonAsync(null, _publishedLessonId);
        Assert.False(locked.Granted);
        Assert.Equal(CourseAccessDenial.AuthenticationRequired, locked.Denial);
    }

    [Fact]
    public async Task AnonymousCannotSeeAnythingUnpublished()
    {
        CourseAccessEvaluator evaluator = Evaluator();

        CourseAccess draftLesson = await evaluator.EvaluateLessonAsync(null, _draftLessonId);
        CourseAccess draftCourse = await evaluator.EvaluateCourseAsync(null, _draftCourseId);

        Assert.Equal(CourseAccessDenial.NotPublished, draftLesson.Denial);
        Assert.Equal(CourseAccessDenial.NotPublished, draftCourse.Denial);
    }

    [Fact]
    public async Task AMissingCourseAndLessonAreReportedAsNotFound()
    {
        CourseAccessEvaluator evaluator = Evaluator();

        Assert.Equal(
            CourseAccessDenial.NotFound,
            (await evaluator.EvaluateCourseAsync(null, Guid.NewGuid())).Denial);
        Assert.Equal(
            CourseAccessDenial.NotFound,
            (await evaluator.EvaluateLessonAsync(null, Guid.NewGuid())).Denial);
    }

    // ---------------------------------------------------------------- admin

    [Fact]
    public async Task AnAdminPreviewsUnpublishedWorkAndIsNeverMistakenForAPurchase()
    {
        Guid adminId = await UserAsync("admin", isAdmin: true);
        CourseAccessEvaluator evaluator = Evaluator();

        CourseAccess draft = await evaluator.EvaluateLessonAsync(adminId, _draftLessonId);

        Assert.True(draft.Granted);
        Assert.Equal(CourseAccessReason.AdminPreview, draft.Reason);
        Assert.Equal("access.granted.adminpreview", draft.Code);

        // The Admin holds no entitlement, so My Learning must stay empty for them.
        Assert.Empty(await evaluator.ListAccessibleCourseIdsAsync(adminId));
    }

    [Fact]
    public async Task TheAdminRoleIsReadFromTheDatabaseNotAClaim()
    {
        Guid student = await UserAsync("not-admin", isAdmin: false);
        CourseAccessEvaluator evaluator = Evaluator();

        CourseAccess draft = await evaluator.EvaluateLessonAsync(student, _draftLessonId);

        Assert.False(draft.Granted);
        Assert.Equal(CourseAccessDenial.NotPublished, draft.Denial);
    }

    // ---------------------------------------------------------------- membership

    [Fact]
    public async Task AMembershipCoversOnlyCoursesFlaggedForMembership()
    {
        Guid member = await UserAsync("member");
        await GrantAsync(member, EntitlementScope.AllMembershipCourses, EntitlementSource.Subscription, null);

        CourseAccessEvaluator evaluator = Evaluator();

        CourseAccess covered = await evaluator.EvaluateLessonAsync(member, _publishedLessonId);
        Assert.True(covered.Granted);
        Assert.Equal(CourseAccessReason.Membership, covered.Reason);
        Assert.True(covered.AllowsResourceDownload);

        // The standalone course is not part of the membership, so it stays locked.
        CourseAccess uncovered = await evaluator.EvaluateLessonAsync(member, _standaloneLessonId);
        Assert.False(uncovered.Granted);
        Assert.Equal(CourseAccessDenial.PurchaseRequired, uncovered.Denial);
        Assert.Equal("access.denied.purchaserequired", uncovered.Code);
    }

    [Fact]
    public async Task ACancelledMembershipKeepsWorkingUntilTheEndOfThePaidPeriod()
    {
        Guid member = await UserAsync("cancelling");

        // Cancellation sets the end of the paid period; it does not revoke.
        await GrantAsync(
            member,
            EntitlementScope.AllMembershipCourses,
            EntitlementSource.Subscription,
            null,
            endsAtUtc: Now.AddDays(10));

        Assert.True((await Evaluator().EvaluateCourseAsync(member, _membershipCourseId)).Granted);

        // The moment the period ends, so does the access — and the reason says so.
        CourseAccess after = await Evaluator(Now.AddDays(11))
            .EvaluateCourseAsync(member, _membershipCourseId);

        Assert.False(after.Granted);
        Assert.Equal(CourseAccessDenial.Expired, after.Denial);
    }

    // ---------------------------------------------------------------- lifetime

    [Fact]
    public async Task ALifetimePurchaseNeverExpiresAndOutranksAMembership()
    {
        Guid member = await UserAsync("owner");
        await GrantAsync(member, EntitlementScope.Course, EntitlementSource.Purchase, _membershipCourseId);
        await GrantAsync(member, EntitlementScope.AllMembershipCourses, EntitlementSource.Subscription, null);

        CourseAccess access = await Evaluator().EvaluateCourseAsync(member, _membershipCourseId);

        // Both cover it, but the durable one is what the member is told about.
        Assert.True(access.Granted);
        Assert.Equal(CourseAccessReason.Lifetime, access.Reason);
        Assert.Null(access.EndsAtUtc);

        // Ten years on it is still theirs.
        Assert.True((await Evaluator(Now.AddYears(10)).EvaluateCourseAsync(member, _membershipCourseId)).Granted);
    }

    [Fact]
    public async Task LifetimeSurvivesTheMembershipItOnceCoexistedWith()
    {
        Guid member = await UserAsync("survivor");
        await GrantAsync(member, EntitlementScope.Course, EntitlementSource.Purchase, _standaloneCourseId);

        Guid membershipId = await GrantAsync(
            member,
            EntitlementScope.AllMembershipCourses,
            EntitlementSource.Subscription,
            null,
            endsAtUtc: Now.AddDays(3));

        // The membership lapses.
        CourseAccessEvaluator later = Evaluator(Now.AddDays(4));

        Assert.False((await later.EvaluateCourseAsync(member, _membershipCourseId)).Granted);

        CourseAccess lifetime = await later.EvaluateCourseAsync(member, _standaloneCourseId);
        Assert.True(lifetime.Granted);
        Assert.Equal(CourseAccessReason.Lifetime, lifetime.Reason);

        Assert.NotEqual(Guid.Empty, membershipId);
    }

    [Fact]
    public async Task RevokingALifetimeGrantLeavesAnUnrelatedMembershipAlone()
    {
        Guid member = await UserAsync("refunded");
        Guid lifetimeId = await GrantAsync(
            member, EntitlementScope.Course, EntitlementSource.Purchase, _standaloneCourseId);
        await GrantAsync(member, EntitlementScope.AllMembershipCourses, EntitlementSource.Subscription, null);

        await RevokeAsync(lifetimeId);

        CourseAccessEvaluator evaluator = Evaluator();

        CourseAccess reversed = await evaluator.EvaluateCourseAsync(member, _standaloneCourseId);
        Assert.False(reversed.Granted);
        Assert.Equal(CourseAccessDenial.Revoked, reversed.Denial);

        // The membership had nothing to do with that purchase.
        CourseAccess untouched = await evaluator.EvaluateCourseAsync(member, _membershipCourseId);
        Assert.True(untouched.Granted);
        Assert.Equal(CourseAccessReason.Membership, untouched.Reason);
    }

    // ---------------------------------------------------------------- manual and development

    [Fact]
    public async Task AComplimentaryGrantIsRecognisedAndReportedAsItself()
    {
        Guid member = await UserAsync("guest");
        await GrantAsync(member, EntitlementScope.Course, EntitlementSource.Manual, _standaloneCourseId);

        CourseAccess access = await Evaluator().EvaluateCourseAsync(member, _standaloneCourseId);

        Assert.True(access.Granted);
        Assert.Equal(CourseAccessReason.ManualGrant, access.Reason);
    }

    [Fact]
    public async Task TheSeededStudentGrantExistsOnlyInExactlyDevelopment()
    {
        await SeedDevelopmentStudentAsync();

        CourseAccess inDevelopment = await Evaluator(environmentName: "Development")
            .EvaluateCourseAsync(SeedIds.DevelopmentStudentUser, _membershipCourseId);

        Assert.True(inDevelopment.Granted);
        Assert.Equal(CourseAccessReason.DevelopmentGrant, inDevelopment.Reason);

        // Casing is not close enough, and neither is any deployed environment.
        foreach (string environmentName in new[] { "development", "DEVELOPMENT", "Staging", "Production" })
        {
            CourseAccess elsewhere = await Evaluator(environmentName: environmentName)
                .EvaluateCourseAsync(SeedIds.DevelopmentStudentUser, _membershipCourseId);

            Assert.False(elsewhere.Granted);
            Assert.Equal(CourseAccessDenial.PurchaseRequired, elsewhere.Denial);
        }
    }

    [Fact]
    public async Task TheDevelopmentGrantIsNotHandedToEveryStudent()
    {
        Guid ordinary = await UserAsync("ordinary");

        CourseAccess access = await Evaluator(environmentName: "Development")
            .EvaluateCourseAsync(ordinary, _membershipCourseId);

        Assert.False(access.Granted);
    }

    // ---------------------------------------------------------------- my learning

    [Fact]
    public async Task MyLearningResolvesMembershipCoursesAtReadTime()
    {
        Guid member = await UserAsync("learner");
        await GrantAsync(member, EntitlementScope.AllMembershipCourses, EntitlementSource.Subscription, null);
        await GrantAsync(member, EntitlementScope.Course, EntitlementSource.Purchase, _standaloneCourseId);

        IReadOnlyList<Guid> courses = await Evaluator().ListAccessibleCourseIdsAsync(member);

        Assert.Contains(_membershipCourseId, courses);
        Assert.Contains(_standaloneCourseId, courses);

        // A draft course is not part of anybody's membership, however it is flagged.
        Assert.DoesNotContain(_draftCourseId, courses);
    }

    [Fact]
    public async Task AnExpiredMemberKeepsNothingInMyLearning()
    {
        Guid member = await UserAsync("lapsed");
        await GrantAsync(
            member,
            EntitlementScope.AllMembershipCourses,
            EntitlementSource.Subscription,
            null,
            endsAtUtc: Now.AddDays(1));

        Assert.NotEmpty(await Evaluator().ListAccessibleCourseIdsAsync(member));
        Assert.Empty(await Evaluator(Now.AddDays(2)).ListAccessibleCourseIdsAsync(member));
    }

    [Fact]
    public async Task AGrantThatHasNotStartedYetDoesNotCount()
    {
        Guid member = await UserAsync("early");
        await GrantAsync(
            member,
            EntitlementScope.Course,
            EntitlementSource.Purchase,
            _standaloneCourseId,
            startsAtUtc: Now.AddDays(5));

        Assert.False((await Evaluator().EvaluateCourseAsync(member, _standaloneCourseId)).Granted);
        Assert.True((await Evaluator(Now.AddDays(6)).EvaluateCourseAsync(member, _standaloneCourseId)).Granted);
    }

    // ---------------------------------------------------------------- helpers

    private CourseAccessEvaluator Evaluator(
        DateTimeOffset? now = null,
        string environmentName = "Production") =>
        new CourseAccessEvaluator(
            fixture.CreateContext(),
            new FixedEnvironment(environmentName),
            new FixedClock(now ?? Now));

    private async Task<Guid> UserAsync(string handle, bool isAdmin = false)
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            IdentityProvider = "Test",
            ExternalIssuer = "https://test.invalid/v2.0",
            ExternalSubjectId = $"subject-{handle}-{Guid.NewGuid():N}",
            Email = $"{handle}@example.test",
            NormalizedEmail = $"{handle}@example.test".ToUpperInvariant(),
            DisplayName = handle,
            EmailVerified = true,
            Status = UserStatus.Active,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        };

        context.Users.Add(user);
        context.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = isAdmin ? SeedIds.AdminRole : SeedIds.StudentRole,
            AssignedAtUtc = Now,
        });

        await context.SaveChangesAsync();

        return user.Id;
    }

    /// <summary>
    /// Issues a grant together with the payment row the schema insists it came from.
    /// </summary>
    /// <remarks>
    /// A purchase grant gets a paid order line and a subscription grant gets a subscription,
    /// because the check constraints refuse the alternative. Writing the tests this way means
    /// the matrix below is exercising access that is genuinely traceable to provider state
    /// rather than an entitlement row conjured out of nothing.
    /// </remarks>
    private async Task<Guid> GrantAsync(
        Guid userId,
        EntitlementScope scope,
        EntitlementSource source,
        Guid? courseId,
        DateTimeOffset? startsAtUtc = null,
        DateTimeOffset? endsAtUtc = null)
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        DateTimeOffset startsAt = startsAtUtc ?? Now.AddDays(-1);

        Guid? orderItemId = null;
        Guid? subscriptionId = null;

        switch (source)
        {
            case EntitlementSource.Purchase:
                OfferPrice lifetime = CommerceFactory.LifetimeOffer(context, courseId!.Value, Now);
                orderItemId = CommerceFactory.PaidOrderItem(
                    context, userId, lifetime, courseId.Value, startsAt);
                break;

            case EntitlementSource.Subscription:
                OfferPrice membership = CommerceFactory.MembershipOffer(
                    context, $"membership-{Guid.NewGuid():N}", Now);
                subscriptionId = CommerceFactory.Subscription(
                    context,
                    userId,
                    membership,
                    startsAt,
                    endsAtUtc ?? startsAt.AddMonths(1),
                    endsAtUtc is null ? SubscriptionStatus.Active : SubscriptionStatus.Canceled,
                    cancelAtPeriodEnd: endsAtUtc is not null);
                break;

            case EntitlementSource.Manual:
            default:
                break;
        }

        var grant = new Entitlement
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Scope = scope,
            Source = source,
            CourseId = courseId,
            OrderItemId = orderItemId,
            SubscriptionId = subscriptionId,
            Status = EntitlementStatus.Active,
            StartsAtUtc = startsAt,
            EndsAtUtc = endsAtUtc,
            GrantReason = source == EntitlementSource.Manual ? "Complimentary access for review." : null,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        };

        context.Entitlements.Add(grant);
        await context.SaveChangesAsync();

        return grant.Id;
    }

    private async Task RevokeAsync(Guid entitlementId)
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Entitlement grant = await context.Entitlements.SingleAsync(entry => entry.Id == entitlementId);
        grant.Status = EntitlementStatus.Revoked;
        grant.RevokedAtUtc = Now;
        grant.RevocationReason = "Full reversal of the original purchase.";
        grant.UpdatedAtUtc = Now;

        await context.SaveChangesAsync();
    }

    private async Task SeedRolesAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        context.Roles.AddRange(
            new Role
            {
                Id = SeedIds.StudentRole,
                Name = "Student",
                NormalizedName = "STUDENT",
                Description = "Customer.",
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now,
            },
            new Role
            {
                Id = SeedIds.AdminRole,
                Name = "Admin",
                NormalizedName = "ADMIN",
                Description = "Operator.",
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now,
            });

        await context.SaveChangesAsync();
    }

    private async Task SeedDevelopmentStudentAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        context.Users.Add(new User
        {
            Id = SeedIds.DevelopmentStudentUser,
            IdentityProvider = "Development",
            ExternalIssuer = "https://development.invalid/v2.0",
            ExternalSubjectId = "development-student",
            Email = "student@danielsdojo.local",
            NormalizedEmail = "STUDENT@DANIELSDOJO.LOCAL",
            DisplayName = "Development Student",
            EmailVerified = true,
            Status = UserStatus.Active,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        });

        context.UserRoles.Add(new UserRole
        {
            UserId = SeedIds.DevelopmentStudentUser,
            RoleId = SeedIds.StudentRole,
            AssignedAtUtc = Now,
        });

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Two published courses — one in the membership, one standalone — plus a draft, so every
    /// membership-coverage rule has something real to be wrong about.
    /// </summary>
    private async Task SeedCatalogAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Course membership = CatalogFactory.Course(
            "membership-course", "Membership course", PublicationStatus.Published, true, Now);
        Course standalone = CatalogFactory.Course(
            "standalone-course", "Standalone course", PublicationStatus.Published, false, Now);
        Course draft = CatalogFactory.Course(
            "draft-course", "Draft course", PublicationStatus.Draft, true, Now);

        context.Courses.AddRange(membership, standalone, draft);

        CourseSection publishedSection =
            CatalogFactory.Section(membership.Id, "Published", 0, PublicationStatus.Published, Now);
        CourseSection standaloneSection =
            CatalogFactory.Section(standalone.Id, "Published", 0, PublicationStatus.Published, Now);
        CourseSection draftSection =
            CatalogFactory.Section(draft.Id, "Draft", 0, PublicationStatus.Draft, Now);

        context.CourseSections.AddRange(publishedSection, standaloneSection, draftSection);

        Lesson published = CatalogFactory.Lesson(
            membership.Id, publishedSection.Id, "paid-lesson", 0,
            PublicationStatus.Published, LessonType.Article, false, "Body.", Now);
        Lesson preview = CatalogFactory.Lesson(
            membership.Id, publishedSection.Id, "free-preview", 1,
            PublicationStatus.Published, LessonType.Article, true, "Body.", Now);
        Lesson standaloneLesson = CatalogFactory.Lesson(
            standalone.Id, standaloneSection.Id, "standalone-lesson", 0,
            PublicationStatus.Published, LessonType.Article, false, "Body.", Now);
        Lesson draftLesson = CatalogFactory.Lesson(
            draft.Id, draftSection.Id, "draft-lesson", 0,
            PublicationStatus.Draft, LessonType.Article, false, "Body.", Now);

        context.Lessons.AddRange(published, preview, standaloneLesson, draftLesson);
        await context.SaveChangesAsync();

        _membershipCourseId = membership.Id;
        _standaloneCourseId = standalone.Id;
        _draftCourseId = draft.Id;
        _publishedLessonId = published.Id;
        _previewLessonId = preview.Id;
        _standaloneLessonId = standaloneLesson.Id;
        _draftLessonId = draftLesson.Id;
    }

    private sealed class FixedEnvironment(string environmentName) : IApplicationEnvironment
    {
        public string EnvironmentName { get; } = environmentName;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
