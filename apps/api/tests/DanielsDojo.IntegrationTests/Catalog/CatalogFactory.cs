using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Infrastructure.Persistence;

namespace DanielsDojo.IntegrationTests.Catalog;

/// <summary>Builds catalog rows for the public and Admin catalog suites.</summary>
internal static class CatalogFactory
{
    public static Course Course(
        string slug,
        string title,
        PublicationStatus status,
        bool includedInMembership,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Title = title,
            Summary = $"{title} summary.",
            Description = $"{title} description.",
            Level = CourseLevel.AllLevels,
            Status = status,
            IncludedInMembership = includedInMembership,
            PublishedAtUtc = status == PublicationStatus.Published ? now : null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

    public static CourseSection Section(
        Guid courseId,
        string title,
        int sortOrder,
        PublicationStatus status,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            CourseId = courseId,
            Title = title,
            SortOrder = sortOrder,
            Status = status,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

    public static Lesson Lesson(
        Guid courseId,
        Guid sectionId,
        string slug,
        int sortOrder,
        PublicationStatus status,
        LessonType lessonType,
        bool isPreview,
        string? body,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            CourseId = courseId,
            CourseSectionId = sectionId,
            Slug = slug,
            Title = slug.Replace('-', ' '),
            LessonType = lessonType,
            BodyMarkdown = body,
            SortOrder = sortOrder,
            IsPreview = isPreview,
            Status = status,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
}

/// <summary>Creates the membership and lifetime commerce rows the catalog prices read from.</summary>
internal static class SeedOffers
{
    public static readonly Guid MembershipOfferId = Guid.Parse("aa000001-0000-4000-8000-000000000001");

    /// <summary>Creates the active monthly membership offer and its 999 USD price.</summary>
    public static async Task CreateAsync(DanielsDojoDbContext context)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow.AddDays(-1);

        context.Offers.Add(new Offer
        {
            Id = MembershipOfferId,
            Code = "membership-monthly-test",
            Name = "Membership",
            Description = "Monthly membership.",
            Kind = OfferKind.Membership,
            Status = CommerceStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        context.Prices.Add(new Price
        {
            Id = Guid.NewGuid(),
            OfferId = MembershipOfferId,
            AmountMinor = 999,
            Currency = "USD",
            BillingInterval = BillingInterval.Month,
            BillingIntervalCount = 1,
            Status = CommerceStatus.Active,
            EffectiveFromUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        await context.SaveChangesAsync();
    }

    /// <summary>Adds an active lifetime offer and 1999 USD price for a course.</summary>
    public static Task AddLifetimeOfferAsync(
        DanielsDojoDbContext context,
        Guid courseId,
        DateTimeOffset now)
    {
        Guid offerId = Guid.NewGuid();

        context.Offers.Add(new Offer
        {
            Id = offerId,
            Code = $"lifetime-{courseId:N}",
            Name = "Lifetime",
            Description = "Lifetime access.",
            Kind = OfferKind.CourseLifetime,
            CourseId = courseId,
            Status = CommerceStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        context.Prices.Add(new Price
        {
            Id = Guid.NewGuid(),
            OfferId = offerId,
            AmountMinor = 1999,
            Currency = "USD",
            BillingInterval = BillingInterval.OneTime,
            BillingIntervalCount = 1,
            Status = CommerceStatus.Active,
            EffectiveFromUtc = now.AddMinutes(-1),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        return Task.CompletedTask;
    }
}
