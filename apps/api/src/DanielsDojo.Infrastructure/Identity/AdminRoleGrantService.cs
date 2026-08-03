using DanielsDojo.Domain.Auditing;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DanielsDojo.Infrastructure.Identity;

/// <summary>Why an administrator grant could not be completed.</summary>
public enum AdminGrantFailure
{
    /// <summary>No failure.</summary>
    None = 0,

    /// <summary>No local user exists with the supplied internal identifier.</summary>
    UserNotFound,

    /// <summary>The seeded Admin role is missing; the reference seed has not been applied.</summary>
    AdminRoleMissing,
}

/// <summary>Result of an administrator grant attempt.</summary>
/// <param name="Failure">Why the grant failed, or <see cref="AdminGrantFailure.None"/>.</param>
/// <param name="RoleWasAdded">
/// True when this run added the role; false when the user already held it. Reruns report false
/// rather than failing, so the command is safe to repeat.
/// </param>
/// <param name="AuditLogId">Identifier of the audit row written in the same transaction.</param>
public sealed record AdminGrantResult(
    AdminGrantFailure Failure,
    bool RoleWasAdded,
    Guid? AuditLogId)
{
    /// <summary>Whether the grant completed.</summary>
    public bool Succeeded => Failure == AdminGrantFailure.None;
}

/// <summary>
/// Grants the seeded Admin role to an existing local user and records the action.
/// </summary>
/// <remarks>
/// The role assignment and the audit row are written in one transaction so they can never
/// diverge — an unaudited privilege escalation is exactly the thing this must prevent. The
/// caller is identified by internal user ID, never by email, because email is not the identity
/// key and could point at the wrong person.
/// </remarks>
public sealed class AdminRoleGrantService(DanielsDojoDbContext context, TimeProvider timeProvider)
{
    /// <summary>Audit action name recorded for this operation.</summary>
    public const string AuditAction = "Identity.AdminRoleGranted";

    /// <summary>Audit target type recorded for this operation.</summary>
    public const string AuditTargetType = "User";

    /// <summary>
    /// Adds the Admin role idempotently, preserving every role the user already holds, and
    /// writes exactly one audit record describing the action.
    /// </summary>
    public async Task<AdminGrantResult> GrantAsync(
        Guid userId,
        string reason,
        string operatorContext,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction =
                await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            bool userExists = await context.Users
                .AnyAsync(user => user.Id == userId, cancellationToken)
                .ConfigureAwait(false);

            if (!userExists)
            {
                return new AdminGrantResult(AdminGrantFailure.UserNotFound, false, null);
            }

            bool adminRoleExists = await context.Roles
                .AnyAsync(role => role.Id == SeedIds.AdminRole, cancellationToken)
                .ConfigureAwait(false);

            if (!adminRoleExists)
            {
                return new AdminGrantResult(AdminGrantFailure.AdminRoleMissing, false, null);
            }

            bool alreadyAdmin = await context.UserRoles
                .AnyAsync(
                    assignment => assignment.UserId == userId
                        && assignment.RoleId == SeedIds.AdminRole,
                    cancellationToken)
                .ConfigureAwait(false);

            DateTimeOffset now = timeProvider.GetUtcNow();

            if (!alreadyAdmin)
            {
                // Added, never replaced: the user keeps Student and anything else they hold.
                context.UserRoles.Add(new UserRole
                {
                    UserId = userId,
                    RoleId = SeedIds.AdminRole,
                    AssignedAtUtc = now,
                    Reason = Truncate(reason, 256),
                });
            }

            AuditLog auditLog = new()
            {
                Id = Guid.NewGuid(),

                // No local actor: this is an out-of-band operator action, and the operator
                // context below records who ran it without inventing a user record.
                ActorUserId = null,
                Action = AuditAction,
                TargetType = AuditTargetType,
                TargetId = userId.ToString(),
                Reason = Truncate(reason, 512),
                OccurredAtUtc = now,
                CorrelationId = Truncate(correlationId, 64),
                MetadataJson = BuildMetadata(operatorContext, alreadyAdmin),
            };

            context.AuditLogs.Add(auditLog);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new AdminGrantResult(AdminGrantFailure.None, !alreadyAdmin, auditLog.Id);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the redacted audit detail. It records the operator context and the outcome only —
    /// no email, display name, token, or other personal data.
    /// </summary>
    private static string BuildMetadata(string operatorContext, bool alreadyAdmin) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            @operator = Truncate(operatorContext, 200),
            outcome = alreadyAdmin ? "AlreadyHeld" : "RoleAdded",
            role = "Admin",
        });

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
