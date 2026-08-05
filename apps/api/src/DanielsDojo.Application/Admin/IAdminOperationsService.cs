using DanielsDojo.Application.Catalog;
using DanielsDojo.Application.Common;

namespace DanielsDojo.Application.Admin;

/// <summary>One platform account as an operator sees it.</summary>
public sealed record AdminUserView(
    Guid Id,
    string DisplayName,
    string Email,
    string Status,
    bool EmailVerified,
    IReadOnlyList<string> Roles,
    int EntitlementCount,
    DateTimeOffset CreatedAtUtc);

/// <summary>Grants or removes the Admin role. The reason is recorded.</summary>
public sealed record SetAdminRoleRequest(bool IsAdmin, string Reason);

/// <summary>Disables or re-enables an account. The reason is recorded.</summary>
public sealed record SetUserStatusRequest(string TargetStatus, string Reason);

/// <summary>Grants one course manually. The reason is recorded.</summary>
public sealed record GrantCourseRequest(Guid CourseId, string Reason);

/// <summary>One issued certificate, for the admin listing.</summary>
public sealed record AdminCertificateView(
    Guid Id,
    string HolderName,
    string CourseTitle,
    string VerificationCode,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string? RevocationReason);

/// <summary>One order, for the admin listing.</summary>
public sealed record AdminOrderView(
    Guid Id,
    string CustomerEmail,
    string OfferName,
    string Status,
    long TotalMinor,
    string Currency,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PaidAtUtc);

/// <summary>One received provider webhook event, for the admin listing.</summary>
public sealed record AdminWebhookEventView(
    Guid Id,
    string Provider,
    string EventType,
    string Status,
    DateTimeOffset ReceivedAtUtc);

/// <summary>One audit row, for the viewer.</summary>
public sealed record AdminAuditEntryView(
    Guid Id,
    string Action,
    string TargetType,
    string TargetId,
    string ActorName,
    string? Reason,
    string? MetadataJson,
    DateTimeOffset OccurredAtUtc);

/// <summary>One operator switch.</summary>
public sealed record FeatureFlagView(
    string Key,
    bool Enabled,
    string Description,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Turns a switch on or off. The reason is recorded.</summary>
public sealed record SetFeatureFlagRequest(bool Enabled, string Reason);

/// <summary>What is actually running, for the ops panel.</summary>
public sealed record OpsSnapshot(
    string EnvironmentName,
    string? InformationalVersion,
    string LastAppliedMigration,
    int PendingMigrationCount,
    string MediaStorageMode,
    string VideoProviderMode,
    string PaymentProviderMode,
    bool DatabaseReachable);

/// <summary>
/// The operator's back office: accounts, records, switches, and what is running.
/// </summary>
/// <remarks>
/// Everything here is read from the same database the application serves from — no cached
/// dashboards — and every mutation takes a reason and lands in the audit trail. Two rules
/// protect the platform from its own operators: an administrator can never remove their own
/// Admin role or disable their own account, so there is always a way back in.
/// </remarks>
public interface IAdminOperationsService
{
    /// <summary>Searches accounts by name or email.</summary>
    Task<PagedResult<AdminUserView>> SearchUsersAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Grants or removes the Admin role. Refuses to act on the caller.</summary>
    Task<OperationResult<AdminUserView>> SetAdminRoleAsync(
        Guid actorUserId, Guid userId, SetAdminRoleRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Disables or re-enables an account. Refuses to act on the caller.</summary>
    Task<OperationResult<AdminUserView>> SetUserStatusAsync(
        Guid actorUserId, Guid userId, SetUserStatusRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Grants a course entitlement manually.</summary>
    Task<OperationResult<AdminUserView>> GrantCourseAsync(
        Guid userId, GrantCourseRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists issued certificates, newest first, optionally filtered by search.</summary>
    Task<PagedResult<AdminCertificateView>> ListCertificatesAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Lists orders, newest first.</summary>
    Task<PagedResult<AdminOrderView>> ListOrdersAsync(
        int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Lists received payment webhook events, newest first.</summary>
    Task<PagedResult<AdminWebhookEventView>> ListWebhookEventsAsync(
        int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Lists audit rows, newest first, optionally filtered by action prefix.</summary>
    Task<PagedResult<AdminAuditEntryView>> ListAuditAsync(
        string? action, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Lists the known switches, including defaults for ones with no stored row.</summary>
    Task<IReadOnlyList<FeatureFlagView>> ListFlagsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Sets a known switch. Unknown keys are refused, not created.</summary>
    Task<OperationResult<FeatureFlagView>> SetFlagAsync(
        string key, SetFeatureFlagRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reports what is actually running.</summary>
    Task<OpsSnapshot> GetOpsSnapshotAsync(CancellationToken cancellationToken = default);
}
