using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Infrastructure.Persistence;

namespace DanielsDojo.IntegrationTests.Database;

/// <summary>
/// A minimal saved graph — user, course, membership offer, course offer, and price — that
/// commerce tests can hang orders, subscriptions, and entitlements from.
/// </summary>
internal sealed class CommerceGraph
{
    public Guid UserId { get; private init; }

    public Guid CourseId { get; private init; }

    public Guid MembershipOfferId { get; private init; }

    public Guid CourseOfferId { get; private init; }

    public Guid PriceId { get; private init; }

    public static async Task<CommerceGraph> CreateAsync(DanielsDojoDbContext context)
    {
        User user = TestEntities.User();
        Course course = TestEntities.Course();
        Offer membershipOffer = TestEntities.MembershipOffer();
        Offer courseOffer = TestEntities.CourseOffer(course.Id);
        Price price = TestEntities.Price(membershipOffer.Id);

        context.Users.Add(user);
        context.Courses.Add(course);
        context.Offers.AddRange(membershipOffer, courseOffer);
        context.Prices.Add(price);
        await context.SaveChangesAsync();

        return new CommerceGraph
        {
            UserId = user.Id,
            CourseId = course.Id,
            MembershipOfferId = membershipOffer.Id,
            CourseOfferId = courseOffer.Id,
            PriceId = price.Id,
        };
    }
}
