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
    Microsoft.Extensions.Options.IOptions<AdminBootstrapOptions> bootstrapOptions,
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

        // A returning sign-in can still consume an unconsumed bootstrap — the designated
        // account may have registered before the configuration was set.
        await TryBootstrapAdminAsync(user, identity, cancellationToken).ConfigureAwait(false);

        return UserProvisioningResult.Success(
            await BuildApplicationUserAsync(user, cancellationToken).ConfigureAwait(false),
            wasProvisioned: false);
    }

    /// <summary>
    /// Grants Admin to the one designated launch administrator, exactly once.
    /// </summary>
    /// <remarks>
    /// Every clause is a distinct defence. The email must match the configured value under
    /// normalization, so casing games change nothing. The provider must assert the address is
    /// verified, so a token minted around verification cannot claim the role. And the grant
    /// only happens while no Admin assignment exists anywhere — the consumed rule — so a
    /// second account arriving later with the same address is just another student. The role
    /// lands on the local user row, which is keyed to the immutable (issuer, subject) pair:
    /// from this point authorization reads that binding and roles, never the email.
    /// </remarks>
    private async Task TryBootstrapAdminAsync(
        User user,
        ExternalUserIdentity identity,
        CancellationToken cancellationToken)
    {
        string configured = bootstrapOptions.Value.BootstrapAdminEmail;

        if (string.IsNullOrWhiteSpace(configured)
            || !identity.EmailVerified
            || string.IsNullOrWhiteSpace(identity.Email)
            || !string.Equals(
                identity.Email.Trim().ToUpperInvariant(),
                configured.Trim().ToUpperInvariant(),
                StringComparison.Ordinal))
        {
            return;
        }

        // Consumed: the bootstrap exists to create the first administrator, not to add more.
        bool anyAdmin = await context.UserRoles
            .AsNoTracking()
            .AnyAsync(assignment => assignment.RoleId == SeedIds.AdminRole, cancellationToken)
            .ConfigureAwait(false);

        if (anyAdmin)
        {
            return;
        }

        AdminRoleGrantService grants = new(context, timeProvider);

        AdminGrantResult result = await grants.GrantAsync(
            user.Id,
            "One-time launch-administrator bootstrap: first verified sign-in of the "
            + "configured bootstrap email. Role bound to the immutable external subject.",
            "bootstrap@first-signin",
            Guid.NewGuid().ToString("N"),
            cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            LogAdminBootstrapped(logger, user.Id);
        }
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

        await TryBootstrapAdminAsync(user, identity, cancellationToken).ConfigureAwait(false);

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

    [LoggerMessage(
        EventId = 3103,
        Level = LogLevel.Information,
        Message = "One-time Admin bootstrap consumed by local user {UserId}.")]
    private static partial void LogAdminBootstrapped(ILogger logger, Guid userId);
}
