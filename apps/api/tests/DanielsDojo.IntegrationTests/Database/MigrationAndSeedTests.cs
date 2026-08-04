using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Database;

/// <summary>
/// Proves the migration applies to an empty database and that both seed profiles are exact,
/// idempotent, and environment-gated.
/// </summary>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class MigrationAndSeedTests(SqlServerDatabaseFixture fixture)
{
    [Fact]
    public async Task InitialPlatformSchema_IsAppliedAndRecordedInInfrastructureSchema()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        string[] applied = [.. await context.Database.GetAppliedMigrationsAsync()];
        string[] pending = [.. await context.Database.GetPendingMigrationsAsync()];

        Assert.Contains(applied, migration => migration.EndsWith("InitialPlatformSchema", StringComparison.Ordinal));
        Assert.Empty(pending);

        // History must live in the infrastructure schema so resets can spare it.
        int historyRows = await CountAsync(
            context,
            "SELECT COUNT(*) AS [Value] FROM [infrastructure].[__EFMigrationsHistory]");
        Assert.True(historyRows >= 1);
    }

    [Fact]
    public async Task AllFiveApplicationSchemasExist()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        foreach (string schema in new[] { "identity", "catalog", "commerce", "learning", "audit" })
        {
            int found = await CountAsync(
                context,
                "SELECT COUNT(*) AS [Value] FROM sys.schemas WHERE name = {0}",
                schema);

            Assert.True(found == 1, $"Expected schema '{schema}' to exist.");
        }
    }

    [Fact]
    public async Task ReferenceSeed_IsIdempotent_AndLeavesRowCountsUnchanged()
    {
        await fixture.ResetWithoutSeedAsync();

        await using (DanielsDojoDbContext first = fixture.CreateContext())
        {
            await SqlServerDatabaseFixture.CreateSeeder(first, "Production").SeedAsync(SeedProfile.Reference);
        }

        (int roles, int courses, int offers, int prices) afterFirst = await CountReferenceAsync();

        await using (DanielsDojoDbContext second = fixture.CreateContext())
        {
            await SqlServerDatabaseFixture.CreateSeeder(second, "Production").SeedAsync(SeedProfile.Reference);
        }

        (int roles, int courses, int offers, int prices) afterSecond = await CountReferenceAsync();

        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(4, afterSecond.roles);
        Assert.Equal(1, afterSecond.courses);
        Assert.Equal(2, afterSecond.offers);
        Assert.Equal(2, afterSecond.prices);
    }

    [Fact]
    public async Task DevelopmentSeed_IsIdempotent_AndLeavesRowCountsUnchanged()
    {
        await fixture.ResetWithoutSeedAsync();

        await using (DanielsDojoDbContext first = fixture.CreateContext())
        {
            await SqlServerDatabaseFixture.CreateSeeder(first, "Development").SeedAsync(SeedProfile.Development);
        }

        (int users, int userRoles, int sections, int lessons) afterFirst = await CountDevelopmentAsync();

        await using (DanielsDojoDbContext second = fixture.CreateContext())
        {
            await SqlServerDatabaseFixture.CreateSeeder(second, "Development").SeedAsync(SeedProfile.Development);
        }

        (int users, int userRoles, int sections, int lessons) afterSecond = await CountDevelopmentAsync();

        Assert.Equal(afterFirst, afterSecond);
        // Two allowlisted Development profiles: the admin holds Admin + Student, the student
        // holds Student only, so both sides of every role gate can be exercised locally.
        Assert.Equal(2, afterSecond.users);
        Assert.Equal(3, afterSecond.userRoles);
        Assert.Equal(2, afterSecond.sections);
        Assert.Equal(4, afterSecond.lessons);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("development")] // Casing must match exactly; near-misses are rejected.
    public async Task DevelopmentSeed_IsRejectedOutsideDevelopment(string environmentName)
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        DatabaseSeeder seeder = SqlServerDatabaseFixture.CreateSeeder(context, environmentName);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => seeder.SeedAsync(SeedProfile.Development));

        Assert.Contains("Development", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReferenceSeed_IsAllowedInAnyEnvironment()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        // No exception: reference data is required everywhere.
        await SqlServerDatabaseFixture.CreateSeeder(context, "Production").SeedAsync(SeedProfile.Reference);
    }

    [Fact]
    public async Task SeededRoles_AreExact()
    {
        await fixture.ResetAsync();
        await using DanielsDojoDbContext context = fixture.CreateContext();

        string[] roles = [.. await context.Roles.Select(role => role.Name).OrderBy(name => name).ToListAsync()];

        Assert.Equal(["Admin", "Instructor", "Student", "Support"], roles);
    }

    [Fact]
    public async Task SeededAtlasCourse_IsExact()
    {
        await fixture.ResetAsync();
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Course course = await context.Courses.SingleAsync(c => c.Id == SeedIds.AtlasCourse);

        Assert.Equal("atlas-enterprise-developer", course.Slug);
        Assert.Equal("Atlas Enterprise Developer", course.Title);
        Assert.True(course.IncludedInMembership);
        Assert.Equal(PublicationStatus.Draft, course.Status);
        Assert.Null(course.PublishedAtUtc);
    }

    [Fact]
    public async Task SeededMembershipOfferAndPrice_AreExact()
    {
        await fixture.ResetAsync();
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Offer offer = await context.Offers.SingleAsync(o => o.Id == SeedIds.MembershipOffer);
        Price price = await context.Prices.SingleAsync(p => p.Id == SeedIds.MembershipMonthlyPrice);

        Assert.Equal(OfferKind.Membership, offer.Kind);
        Assert.Equal(CommerceStatus.Active, offer.Status);
        Assert.Null(offer.CourseId);
        Assert.Null(offer.StripeProductId);

        Assert.Equal(999, price.AmountMinor);
        Assert.Equal("USD", price.Currency);
        Assert.Equal(BillingInterval.Month, price.BillingInterval);
        Assert.Equal(1, price.BillingIntervalCount);
        Assert.Equal(CommerceStatus.Active, price.Status);
        Assert.Null(price.StripePriceId);
    }

    [Fact]
    public async Task SeededAtlasLifetimeOfferAndPrice_AreExact()
    {
        await fixture.ResetAsync();
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Offer offer = await context.Offers.SingleAsync(o => o.Id == SeedIds.AtlasLifetimeOffer);
        Price price = await context.Prices.SingleAsync(p => p.Id == SeedIds.AtlasLifetimePrice);

        Assert.Equal(OfferKind.CourseLifetime, offer.Kind);
        Assert.Equal(SeedIds.AtlasCourse, offer.CourseId);
        Assert.Equal(CommerceStatus.Active, offer.Status);
        Assert.Null(offer.StripeProductId);

        Assert.Equal(1999, price.AmountMinor);
        Assert.Equal("USD", price.Currency);
        Assert.Equal(BillingInterval.OneTime, price.BillingInterval);
        Assert.Equal(CommerceStatus.Active, price.Status);
        Assert.Null(price.StripePriceId);
    }

    [Fact]
    public async Task DevelopmentSeed_MarksExactlyOnePreviewLesson_AndCreatesNoCommerceRows()
    {
        await fixture.ResetWithoutSeedAsync();

        await using (DanielsDojoDbContext seedContext = fixture.CreateContext())
        {
            await SqlServerDatabaseFixture.CreateSeeder(seedContext, "Development").SeedAsync(SeedProfile.Development);
        }

        await using DanielsDojoDbContext context = fixture.CreateContext();

        Assert.Equal(1, await context.Lessons.CountAsync(lesson => lesson.IsPreview));
        Assert.Equal(2, await context.Lessons.CountAsync(lesson => lesson.LessonType == LessonType.Video));
        Assert.Equal(2, await context.Lessons.CountAsync(lesson => lesson.LessonType == LessonType.Article));
        Assert.True(await context.Lessons.AllAsync(lesson => lesson.Status == PublicationStatus.Draft));

        // The development profile installs authoring data only — never money or progress.
        Assert.Equal(0, await context.Orders.CountAsync());
        Assert.Equal(0, await context.Subscriptions.CountAsync());
        Assert.Equal(0, await context.Entitlements.CountAsync());
        Assert.Equal(0, await context.Refunds.CountAsync());
        Assert.Equal(0, await context.PaymentDisputes.CountAsync());
        Assert.Equal(0, await context.WebhookEvents.CountAsync());
        Assert.Equal(0, await context.LessonProgress.CountAsync());
        Assert.Equal(0, await context.Enrollments.CountAsync());

        // Forum categories are structure, not content. No fake member activity is ever seeded.
        Assert.Equal(3, await context.ForumCategories.CountAsync());
        Assert.Equal(0, await context.ForumThreads.CountAsync());
        Assert.Equal(0, await context.ForumPosts.CountAsync());
        Assert.Equal(0, await context.DirectMessages.CountAsync());
        Assert.Equal(0, await context.FriendRequests.CountAsync());
        Assert.Equal(0, await context.Notifications.CountAsync());
        Assert.Equal(0, await context.Reports.CountAsync());

        // Profiles are created by the member during setup, never seeded on their behalf.
        Assert.Equal(0, await context.CommunityProfiles.CountAsync());
    }

    [Fact]
    public async Task ReferenceSeed_CreatesNoUsersOrCommunityContent()
    {
        await fixture.ResetWithoutSeedAsync();

        await using (DanielsDojoDbContext seedContext = fixture.CreateContext())
        {
            await SqlServerDatabaseFixture.CreateSeeder(seedContext, "Production")
                .SeedAsync(SeedProfile.Reference);
        }

        await using DanielsDojoDbContext context = fixture.CreateContext();

        // Reference data is safe for any environment: it must never invent a person.
        Assert.Equal(0, await context.Users.CountAsync());
        Assert.Equal(0, await context.CommunityProfiles.CountAsync());
        Assert.Equal(0, await context.ForumCategories.CountAsync());
        Assert.Equal(0, await context.ForumThreads.CountAsync());
    }

    [Fact]
    public async Task Respawn_ClearsApplicationRows_ButPreservesMigrationHistory()
    {
        await fixture.ResetAsync();

        await using (DanielsDojoDbContext seeded = fixture.CreateContext())
        {
            Assert.True(await seeded.Roles.AnyAsync());
        }

        await fixture.ResetWithoutSeedAsync();

        await using DanielsDojoDbContext context = fixture.CreateContext();

        Assert.False(await context.Roles.AnyAsync());
        Assert.False(await context.Courses.AnyAsync());

        // The database must still look migrated, not freshly created.
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.True(await CountAsync(
            context,
            "SELECT COUNT(*) AS [Value] FROM [infrastructure].[__EFMigrationsHistory]") >= 1);

        await fixture.ResetAsync();
    }

    private async Task<(int Roles, int Courses, int Offers, int Prices)> CountReferenceAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        return (
            await context.Roles.CountAsync(),
            await context.Courses.CountAsync(),
            await context.Offers.CountAsync(),
            await context.Prices.CountAsync());
    }

    private async Task<(int Users, int UserRoles, int Sections, int Lessons)> CountDevelopmentAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        return (
            await context.Users.CountAsync(),
            await context.UserRoles.CountAsync(),
            await context.CourseSections.CountAsync(),
            await context.Lessons.CountAsync());
    }

    /// <summary>
    /// Runs a scalar count. SqlQueryRaw projects a column named "Value", so every query here
    /// aliases it. Parameters are always bound, never interpolated into the SQL text.
    /// </summary>
    private static async Task<int> CountAsync(
        DanielsDojoDbContext context,
        string sql,
        params object[] parameters)
    {
        return await context.Database.SqlQueryRaw<int>(sql, parameters).SingleAsync();
    }
}
