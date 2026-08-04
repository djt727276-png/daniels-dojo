using DanielsDojo.Application.System;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Domain.Community;
using DanielsDojo.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace DanielsDojo.Infrastructure.Persistence.Seeding;

/// <summary>
/// Installs seed rows explicitly. This is never invoked by ordinary application startup —
/// it runs only from the database CLI or from tests.
/// </summary>
/// <remarks>
/// Seeding is deliberately hand-written rather than expressed with EF Core's
/// <c>HasData</c>: the catalog and commerce rows are operator-editable, and
/// <c>HasData</c> would generate migrations that overwrite live edits to titles and amounts.
/// Every write here is insert-if-absent against a deterministic key, so a rerun changes
/// nothing that a human has since changed.
/// </remarks>
public sealed partial class DatabaseSeeder(
    DanielsDojoDbContext context,
    IApplicationEnvironment environment,
    TimeProvider timeProvider,
    ILogger<DatabaseSeeder> logger)
{
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Seed profile {Profile} applied. Rows written: {RowsWritten}.")]
    private static partial void LogSeedApplied(ILogger logger, SeedProfile profile, int rowsWritten);

    /// <summary>The only environment in which the Development profile may run.</summary>
    public const string DevelopmentEnvironmentName = "Development";

    /// <summary>
    /// Tenant value recorded as <c>ExternalIssuer</c> for the local development accounts.
    /// The Development authentication harness stamps the same value into its tokens' <c>tid</c>
    /// claim so the existing (tid, oid) provisioning lookup resolves these seeded rows.
    /// </summary>
    public const string DevelopmentSeedIssuer = "00000000-0000-4000-8000-0000000d0d00";

    /// <summary>Subject recorded for the deterministic local development administrator.</summary>
    public const string DevelopmentSeedSubject = "development-admin";

    /// <summary>Subject recorded for the deterministic local development student.</summary>
    public const string DevelopmentSeedStudentSubject = "development-student";

    /// <summary>Email of the deterministic local development administrator.</summary>
    public const string DevelopmentAdminEmail = "admin@danielsdojo.local";

    /// <summary>Email of the deterministic local development student.</summary>
    public const string DevelopmentStudentEmail = "student@danielsdojo.local";

    /// <summary>
    /// Applies the requested profile inside a single transaction. Reruns are safe and leave
    /// row counts unchanged.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="SeedProfile.Development"/> is requested outside the Development
    /// environment. The seeder fails closed rather than installing sample accounts.
    /// </exception>
    public async Task SeedAsync(SeedProfile profile, CancellationToken cancellationToken = default)
    {
        GuardProfileAllowed(profile);

        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction =
                await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            DateTimeOffset now = timeProvider.GetUtcNow();

            await SeedReferenceAsync(now, cancellationToken).ConfigureAwait(false);

            if (profile == SeedProfile.Development)
            {
                await SeedDevelopmentAsync(now, cancellationToken).ConfigureAwait(false);
            }

            int written = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            LogSeedApplied(logger, profile, written);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Throws unless the profile is permitted in the current environment. Exposed so the CLI
    /// can reject an unsafe request before it touches the database at all.
    /// </summary>
    public void GuardProfileAllowed(SeedProfile profile)
    {
        if (profile != SeedProfile.Development)
        {
            return;
        }

        if (!string.Equals(
                environment.EnvironmentName,
                DevelopmentEnvironmentName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The Development seed profile may only run when the host environment is exactly " +
                $"'{DevelopmentEnvironmentName}'. The current environment is " +
                $"'{environment.EnvironmentName}'. Use the Reference profile instead.");
        }
    }

    private async Task SeedReferenceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await AddRoleIfAbsentAsync(
            SeedIds.StudentRole, "Student", "Default role for every learner.", cancellationToken)
            .ConfigureAwait(false);
        await AddRoleIfAbsentAsync(
            SeedIds.AdminRole, "Admin", "Full administrative access to the platform.", cancellationToken)
            .ConfigureAwait(false);
        await AddRoleIfAbsentAsync(
            SeedIds.InstructorRole, "Instructor", "Authors and maintains course content.", cancellationToken)
            .ConfigureAwait(false);
        await AddRoleIfAbsentAsync(
            SeedIds.SupportRole, "Support", "Handles customer support and access reviews.", cancellationToken)
            .ConfigureAwait(false);

        // Course shell only. Sections and lessons are authoring work, not reference data.
        if (!await context.Courses
                .AnyAsync(course => course.Id == SeedIds.AtlasCourse, cancellationToken)
                .ConfigureAwait(false))
        {
            context.Courses.Add(new Course
            {
                Id = SeedIds.AtlasCourse,
                Slug = "atlas-enterprise-developer",
                Title = "Atlas Enterprise Developer",
                Summary = "Build, ship, and operate enterprise applications on the Atlas platform.",
                Description =
                    "The Atlas Enterprise Developer course covers building, shipping, and operating " +
                    "enterprise applications end to end. Content authoring is in progress.",
                Level = CourseLevel.AllLevels,
                Status = PublicationStatus.Draft,
                IncludedInMembership = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        await AddOfferIfAbsentAsync(
            SeedIds.MembershipOffer,
            code: "membership-monthly",
            name: "Daniel's Dojo Membership",
            description: "Monthly all-access membership covering every course included in membership.",
            kind: OfferKind.Membership,
            courseId: null,
            now,
            cancellationToken).ConfigureAwait(false);

        await AddOfferIfAbsentAsync(
            SeedIds.AtlasLifetimeOffer,
            code: "atlas-enterprise-developer-lifetime",
            name: "Atlas Enterprise Developer — Lifetime",
            description: "One-time purchase granting lifetime access to Atlas Enterprise Developer.",
            kind: OfferKind.CourseLifetime,
            courseId: SeedIds.AtlasCourse,
            now,
            cancellationToken).ConfigureAwait(false);

        await AddPriceIfAbsentAsync(
            SeedIds.MembershipMonthlyPrice,
            offerId: SeedIds.MembershipOffer,
            amountMinor: 999,
            interval: BillingInterval.Month,
            now,
            cancellationToken).ConfigureAwait(false);

        await AddPriceIfAbsentAsync(
            SeedIds.AtlasLifetimePrice,
            offerId: SeedIds.AtlasLifetimeOffer,
            amountMinor: 1999,
            interval: BillingInterval.OneTime,
            now,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SeedDevelopmentAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Two allowlisted profiles, each with a fixed identity so the Development
        // authentication harness can resolve them through the ordinary (tid, oid) lookup.
        await AddDevelopmentUserIfAbsentAsync(
            SeedIds.DevelopmentAdminUser,
            DevelopmentSeedSubject,
            DevelopmentAdminEmail,
            "Development Admin",
            now,
            cancellationToken).ConfigureAwait(false);

        await AddDevelopmentUserIfAbsentAsync(
            SeedIds.DevelopmentStudentUser,
            DevelopmentSeedStudentSubject,
            DevelopmentStudentEmail,
            "Development Student",
            now,
            cancellationToken).ConfigureAwait(false);

        await AddUserRoleIfAbsentAsync(
            SeedIds.DevelopmentAdminUser, SeedIds.AdminRole, now, cancellationToken)
            .ConfigureAwait(false);
        await AddUserRoleIfAbsentAsync(
            SeedIds.DevelopmentAdminUser, SeedIds.StudentRole, now, cancellationToken)
            .ConfigureAwait(false);

        // The student profile deliberately holds Student only, so the two profiles exercise
        // both sides of every role gate.
        await AddUserRoleIfAbsentAsync(
            SeedIds.DevelopmentStudentUser, SeedIds.StudentRole, now, cancellationToken)
            .ConfigureAwait(false);

        await SeedForumCategoriesAsync(now, cancellationToken).ConfigureAwait(false);

        await AddSectionIfAbsentAsync(
            SeedIds.AtlasSectionOne, "Getting Started", sortOrder: 1, now, cancellationToken)
            .ConfigureAwait(false);
        await AddSectionIfAbsentAsync(
            SeedIds.AtlasSectionTwo, "Building and Shipping", sortOrder: 2, now, cancellationToken)
            .ConfigureAwait(false);

        // Exactly one lesson is marked preview, so the free-preview path has local coverage.
        await AddLessonIfAbsentAsync(
            SeedIds.AtlasLessonWelcome,
            SeedIds.AtlasSectionOne,
            slug: "welcome-to-atlas",
            title: "Welcome to Atlas Enterprise Developer",
            LessonType.Video,
            sortOrder: 1,
            isPreview: true,
            body: null,
            now,
            cancellationToken).ConfigureAwait(false);

        await AddLessonIfAbsentAsync(
            SeedIds.AtlasLessonSetup,
            SeedIds.AtlasSectionOne,
            slug: "setting-up-your-environment",
            title: "Setting Up Your Environment",
            LessonType.Article,
            sortOrder: 2,
            isPreview: false,
            body: "# Setting Up Your Environment\n\nSample development content.",
            now,
            cancellationToken).ConfigureAwait(false);

        await AddLessonIfAbsentAsync(
            SeedIds.AtlasLessonStructure,
            SeedIds.AtlasSectionTwo,
            slug: "solution-structure",
            title: "Solution Structure",
            LessonType.Video,
            sortOrder: 1,
            isPreview: false,
            body: null,
            now,
            cancellationToken).ConfigureAwait(false);

        await AddLessonIfAbsentAsync(
            SeedIds.AtlasLessonDeployment,
            SeedIds.AtlasSectionTwo,
            slug: "deployment-checklist",
            title: "Deployment Checklist",
            LessonType.Article,
            sortOrder: 2,
            isPreview: false,
            body: "# Deployment Checklist\n\nSample development content.",
            now,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Installs the starter forum categories. Categories are structural, not user content —
    /// no fake threads, posts, or messages are ever seeded.
    /// </summary>
    private async Task SeedForumCategoriesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await AddForumCategoryIfAbsentAsync(
            SeedIds.AnnouncementsForumCategory,
            "announcements",
            "Announcements",
            "Platform and course news from the Daniel's Dojo team.",
            sortOrder: 1,
            now,
            cancellationToken).ConfigureAwait(false);

        await AddForumCategoryIfAbsentAsync(
            SeedIds.GeneralForumCategory,
            "general",
            "General Discussion",
            "Introductions, wins, and anything else that does not fit elsewhere.",
            sortOrder: 2,
            now,
            cancellationToken).ConfigureAwait(false);

        await AddForumCategoryIfAbsentAsync(
            SeedIds.CourseHelpForumCategory,
            "course-help",
            "Course Help",
            "Questions about course material and exercises.",
            sortOrder: 3,
            now,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task AddForumCategoryIfAbsentAsync(
        Guid id,
        string slug,
        string name,
        string description,
        int sortOrder,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await context.ForumCategories.AnyAsync(category => category.Id == id, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        context.ForumCategories.Add(new ForumCategory
        {
            Id = id,
            Slug = slug,
            Name = name,
            Description = description,
            SortOrder = sortOrder,
            Status = ForumCategoryStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
    }

    private async Task AddDevelopmentUserIfAbsentAsync(
        Guid id,
        string externalSubjectId,
        string email,
        string displayName,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await context.Users.AnyAsync(user => user.Id == id, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        context.Users.Add(new User
        {
            Id = id,
            IdentityProvider = "DevelopmentSeed",
            ExternalIssuer = DevelopmentSeedIssuer,
            ExternalSubjectId = externalSubjectId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = displayName,
            EmailVerified = true,
            Status = UserStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
    }

    private async Task AddRoleIfAbsentAsync(
        Guid id,
        string name,
        string description,
        CancellationToken cancellationToken)
    {
        if (await context.Roles.AnyAsync(role => role.Id == id, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        context.Roles.Add(new Role
        {
            Id = id,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Description = description,
            IsAssignable = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
    }

    private async Task AddOfferIfAbsentAsync(
        Guid id,
        string code,
        string name,
        string description,
        OfferKind kind,
        Guid? courseId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await context.Offers.AnyAsync(offer => offer.Id == id, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        context.Offers.Add(new Offer
        {
            Id = id,
            Code = code,
            Name = name,
            Description = description,
            Kind = kind,
            CourseId = courseId,

            // Provider identifiers stay null until the offer is created at Stripe.
            StripeProductId = null,
            Status = CommerceStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
    }

    private async Task AddPriceIfAbsentAsync(
        Guid id,
        Guid offerId,
        long amountMinor,
        BillingInterval interval,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await context.Prices.AnyAsync(price => price.Id == id, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        context.Prices.Add(new Price
        {
            Id = id,
            OfferId = offerId,
            AmountMinor = amountMinor,
            Currency = "USD",
            BillingInterval = interval,
            BillingIntervalCount = 1,
            StripePriceId = null,
            Status = CommerceStatus.Active,
            EffectiveFromUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
    }

    private async Task AddUserRoleIfAbsentAsync(
        Guid userId,
        Guid roleId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        bool exists = await context.UserRoles
            .AnyAsync(
                userRole => userRole.UserId == userId && userRole.RoleId == roleId,
                cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return;
        }

        context.UserRoles.Add(new UserRole
        {
            UserId = userId,
            RoleId = roleId,
            AssignedAtUtc = now,
            Reason = "Seeded for local development.",
        });
    }

    private async Task AddSectionIfAbsentAsync(
        Guid id,
        string title,
        int sortOrder,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await context.CourseSections.AnyAsync(section => section.Id == id, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        context.CourseSections.Add(new CourseSection
        {
            Id = id,
            CourseId = SeedIds.AtlasCourse,
            Title = title,
            SortOrder = sortOrder,
            Status = PublicationStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
    }

    private async Task AddLessonIfAbsentAsync(
        Guid id,
        Guid sectionId,
        string slug,
        string title,
        LessonType lessonType,
        int sortOrder,
        bool isPreview,
        string? body,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await context.Lessons.AnyAsync(lesson => lesson.Id == id, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        context.Lessons.Add(new Lesson
        {
            Id = id,
            CourseId = SeedIds.AtlasCourse,
            CourseSectionId = sectionId,
            Slug = slug,
            Title = title,
            LessonType = lessonType,
            BodyMarkdown = body,
            SortOrder = sortOrder,
            IsPreview = isPreview,
            Status = PublicationStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
    }
}
