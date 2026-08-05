using DanielsDojo.Application.Community;
using DanielsDojo.Domain.Community;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DanielsDojo.Infrastructure.Community;

/// <summary>
/// Decides community participation from the local database.
/// </summary>
/// <remarks>
/// The rules today are: the platform account is active, a community profile exists, it is not
/// suspended or deactivated, and the member has accepted the guidelines and attested
/// eligibility. A later phase adds a qualifying-entitlement requirement here — one method, one
/// place — rather than in each of the twenty-odd endpoints that consult it.
/// </remarks>
internal sealed class CommunityAccessEvaluator(DanielsDojoDbContext context)
    : ICommunityAccessEvaluator
{
    public async Task<CommunityAccess> EvaluateAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await (
            from user in context.Users.AsNoTracking()
            where user.Id == userId
            select new
            {
                user.Status,
                Profile = context.CommunityProfiles
                    .AsNoTracking()
                    .FirstOrDefault(profile => profile.UserId == userId),
            }).FirstOrDefaultAsync(cancellationToken);

        if (snapshot is null || snapshot.Status != UserStatus.Active)
        {
            return new CommunityAccess(
                false, CommunityAccessDenial.AccountDisabled, ProfileExists: false, Handle: null);
        }

        CommunityProfile? profile = snapshot.Profile;

        if (profile is null)
        {
            return new CommunityAccess(
                false, CommunityAccessDenial.SetupRequired, ProfileExists: false, Handle: null);
        }

        CommunityAccessDenial denial = profile.Status switch
        {
            CommunityProfileStatus.Suspended => CommunityAccessDenial.Suspended,
            CommunityProfileStatus.Deactivated => CommunityAccessDenial.Deactivated,
            _ when !profile.IsParticipationReady => CommunityAccessDenial.SetupRequired,
            _ => CommunityAccessDenial.None,
        };

        // The operator kill switch is checked last, so an individual denial keeps its more
        // specific message. A missing row means the default: writes are on.
        if (denial == CommunityAccessDenial.None
            && await context.FeatureFlags
                .AsNoTracking()
                .Where(flag => flag.Key == "community-writes")
                .Select(flag => (bool?)flag.Enabled)
                .FirstOrDefaultAsync(cancellationToken) == false)
        {
            denial = CommunityAccessDenial.WritesPaused;
        }

        return new CommunityAccess(
            denial == CommunityAccessDenial.None,
            denial,
            ProfileExists: true,
            profile.Handle);
    }
}
