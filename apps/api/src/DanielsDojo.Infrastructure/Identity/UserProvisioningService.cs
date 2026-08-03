using DanielsDojo.Application.Identity;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DanielsDojo.Infrastructure.Identity;

/// <summary>
/// Maps an Entra External ID identity onto the Phase 2 <c>identity.Users</c> table.
/// </summary>
/// <remarks>
/// The ownership key is the immutable pair (<c>tid</c>, <c>oid</c>), stored in the Phase 2
/// columns <c>ExternalIssuer</c> and <c>ExternalSubjectId</c> and protected by that table's
/// unique index. Email is explicitly not the key: a customer can change their address, and two
/// provider identities may legitimately present the same one, so keying on it would let one
/// person take over another's account.
/// </remarks>
public sealed partial class UserProvisioningService(
    DanielsDojoDbContext context,
    TimeProvider timeProvider,
    ILogger<UserProvisioningService> logger) : IUserProvisioningService
{
    /// <summary>Provider name recorded on locally provisioned users.</summary>
    public const string IdentityProviderName = "EntraExternalId";

    private const int MaxDisplayNameLength = 128;
    private const int MaxEmailLength = 256;

    /// <inheritdoc />
    public async Task<UserProvisioningResult> ResolveAsync(
        ExternalUserIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (string.IsNullOrWhiteSpace(identity.TenantId) || string.IsNullOrWhiteSpace(identity.ObjectId))
        {
            return UserProvisioningResult.Denied(UserProvisioningFailure.MissingIdentityClaims);
        }

        User? user = await FindAsync(identity, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            // A brand-new customer must arrive with an email: it is the only way to contact them
            // about a purchase, and Phase 2 requires the column.
            if (string.IsNullOrWhiteSpace(identity.Email))
            {
                return UserProvisioningResult.Denied(UserProvisioningFailure.MissingEmailClaim);
            }

            return await ProvisionAsync(identity, cancellationToken).ConfigureAwait(false);
        }

        if (user.Status == UserStatus.Disabled)
        {
            return UserProvisioningResult.Denied(UserProvisioningFailure.UserDisabled);
        }

        await SynchronizeProfileAsync(user, identity, cancellationToken).ConfigureAwait(false);

        return UserProvisioningResult.Success(
            await BuildApplicationUserAsync(user, cancellationToken).ConfigureAwait(false),
            wasProvisioned: false);
    }

    private Task<User?> FindAsync(ExternalUserIdentity identity, CancellationToken cancellationToken) =>
        context.Users.SingleOrDefaultAsync(
            candidate => candidate.ExternalIssuer == identity.TenantId
                && candidate.ExternalSubjectId == identity.ObjectId,
            cancellationToken);

    private async Task<UserProvisioningResult> ProvisionAsync(
        ExternalUserIdentity identity,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        string email = Truncate(identity.Email!, MaxEmailLength);

        User user = new()
        {
            Id = Guid.NewGuid(),
            IdentityProvider = IdentityProviderName,
            ExternalIssuer = identity.TenantId,
            ExternalSubjectId = identity.ObjectId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = Truncate(
                string.IsNullOrWhiteSpace(identity.DisplayName) ? email : identity.DisplayName,
                MaxDisplayNameLength),
            EmailVerified = identity.EmailVerified,
            Status = UserStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.Users.Add(user);

        // Every new customer gets exactly Student. Nothing else is ever granted implicitly.
        context.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = SeedIds.StudentRole,
            AssignedAtUtc = now,
            Reason = "Assigned automatically on first sign-in.",
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Two first requests for the same identity raced. The unique index on
            // (ExternalIssuer, ExternalSubjectId) means exactly one insert won; discard the
            // loser's tracked state and read the winner rather than returning a 500 or
            // creating a duplicate role. Any other update failure is a genuine fault and is
            // rethrown untouched.
            context.ChangeTracker.Clear();

            User? winner = await FindAsync(identity, cancellationToken).ConfigureAwait(false);
            if (winner is null)
            {
                throw;
            }

            LogProvisioningRaceResolved(logger);

            if (winner.Status == UserStatus.Disabled)
            {
                return UserProvisioningResult.Denied(UserProvisioningFailure.UserDisabled);
            }

            return UserProvisioningResult.Success(
                await BuildApplicationUserAsync(winner, cancellationToken).ConfigureAwait(false),
                wasProvisioned: false);
        }

        LogUserProvisioned(logger, user.Id);

        return UserProvisioningResult.Success(
            await BuildApplicationUserAsync(user, cancellationToken).ConfigureAwait(false),
            wasProvisioned: true);
    }

    private Task<bool> ExistsAsync(ExternalUserIdentity identity, CancellationToken cancellationToken) =>
        context.Users
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.ExternalIssuer == identity.TenantId
                    && candidate.ExternalSubjectId == identity.ObjectId,
                cancellationToken);

    /// <summary>
    /// Refreshes safe mutable profile fields on later sign-ins. Identity columns and role
    /// assignments are never touched here — a returning administrator must stay an
    /// administrator, and no token content may alter who the record belongs to.
    /// </summary>
    private async Task SynchronizeProfileAsync(
        User user,
        ExternalUserIdentity identity,
        CancellationToken cancellationToken)
    {
        bool changed = false;

        if (!string.IsNullOrWhiteSpace(identity.Email))
        {
            string email = Truncate(identity.Email, MaxEmailLength);
            if (!string.Equals(user.Email, email, StringComparison.Ordinal))
            {
                user.Email = email;
                user.NormalizedEmail = email.ToUpperInvariant();
                changed = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(identity.DisplayName))
        {
            string displayName = Truncate(identity.DisplayName, MaxDisplayNameLength);
            if (!string.Equals(user.DisplayName, displayName, StringComparison.Ordinal))
            {
                user.DisplayName = displayName;
                changed = true;
            }
        }

        if (user.EmailVerified != identity.EmailVerified)
        {
            user.EmailVerified = identity.EmailVerified;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        user.UpdatedAtUtc = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ApplicationUser> BuildApplicationUserAsync(
        User user,
        CancellationToken cancellationToken)
    {
        string[] roleNames = await context.UserRoles
            .AsNoTracking()
            .Where(assignment => assignment.UserId == user.Id)
            .Join(
                context.Roles.AsNoTracking(),
                assignment => assignment.RoleId,
                role => role.Id,
                (_, role) => role.Name)
            .OrderBy(name => name)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ApplicationUser(user.Id, user.DisplayName, user.Email, roleNames);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Provisioned local user {UserId} on first sign-in.")]
    private static partial void LogUserProvisioned(ILogger logger, Guid userId);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Information,
        Message = "Concurrent first sign-in detected; reloaded the winning user record.")]
    private static partial void LogProvisioningRaceResolved(ILogger logger);
}
