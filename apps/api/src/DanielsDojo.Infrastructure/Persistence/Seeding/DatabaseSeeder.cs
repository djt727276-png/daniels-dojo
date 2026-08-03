using DanielsDojo.Application.System;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
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

    /// <summary>Issuer recorded for the deterministic local development account.</summary>
    public const string DevelopmentSeedIssuer = "https://seed.danielsdojo.local/development";

    /// <summary>Subject recorded for the deterministic local development account.</summary>
    public const string DevelopmentSeedSubject = "development-admin";

    /// <summary>Email of the deterministic local development administrator.</summary>
    public const string DevelopmentAdminEmail = "admin@danielsdojo.local";

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
        bool adminExists = await context.Users
            .AnyAsync(user => user.Id == SeedIds.DevelopmentAdminUser, cancellationToken)
            .ConfigureAwait(false);

        if (!adminExists)
        {
            context.Users.Add(new User
            {
                Id = SeedIds.DevelopmentAdminUser,
                IdentityProvider = "DevelopmentSeed",
                ExternalIssuer = DevelopmentSeedIssuer,
                ExternalSubjectId = DevelopmentSeedSubject,
                Email = DevelopmentAdminEmail,
                NormalizedEmail = DevelopmentAdminEmail.ToUpperInvariant(),
                DisplayName = "Development Admin",
                EmailVerified = true,
                Status = UserStatus.Active,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        await AddUserRoleIfAbsentAsync(
            SeedIds.DevelopmentAdminUser, SeedIds.AdminRole, now, cancellationToken)
            .ConfigureAwait(false);
        await AddUserRoleIfAbsentAsync(
            SeedIds.DevelopmentAdminUser, SeedIds.StudentRole, now, cancellationToken)
            .ConfigureAwait(false);

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
