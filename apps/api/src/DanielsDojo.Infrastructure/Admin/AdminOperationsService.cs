using System.Globalization;
using System.Reflection;
using DanielsDojo.Application.Admin;
using DanielsDojo.Application.Catalog;
using DanielsDojo.Application.Commerce;
using DanielsDojo.Application.Common;
using DanielsDojo.Application.Identity;
using DanielsDojo.Application.Media;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Infrastructure.Auditing;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DanielsDojo.Infrastructure.Admin;

/// <summary>
/// The operator's back office.
/// </summary>
/// <remarks>
/// Everything reads from the live database; every mutation takes a reason and lands in the
/// audit trail. The self-protection rules — no removing your own Admin role, no disabling
/// your own account — exist so an operator can never lock the platform's last key inside it.
/// </remarks>
internal sealed class AdminOperationsService : IAdminOperationsService
{
    private const int MaxPageSize = 50;

    /// <summary>
    /// The switches that exist. A key outside this set cannot be created, so the flag list
    /// can never silently grow switches nothing reads.
    /// </summary>
    private static readonly Dictionary<string, string> KnownFlags =
        new(StringComparer.Ordinal)
        {
            ["checkout"] = "Customer purchasing. Off refuses new checkouts; access already granted is untouched.",
            ["community-writes"] = "Community posting. Off refuses new threads, replies, and messages; reading is untouched.",
        };

    private readonly DanielsDojoDbContext context;
    private readonly TimeProvider timeProvider;
    private readonly IHostEnvironment environment;
    private readonly MediaStorageOptions mediaOptions;
    private readonly VideoProviderOptions videoOptions;
    private readonly PaymentProviderOptions paymentOptions;
    private readonly AuditTrail audit;

    public AdminOperationsService(
        DanielsDojoDbContext context,
        IOperationContext operationContext,
        TimeProvider timeProvider,
        IHostEnvironment environment,
        IOptions<MediaStorageOptions> mediaOptions,
        IOptions<VideoProviderOptions> videoOptions,
        IOptions<PaymentProviderOptions> paymentOptions)
    {
        this.context = context;
        this.timeProvider = timeProvider;
        this.environment = environment;
        this.mediaOptions = mediaOptions.Value;
        this.videoOptions = videoOptions.Value;
        this.paymentOptions = paymentOptions.Value;

        audit = new AuditTrail(context, operationContext, timeProvider);
    }

    // ------------------------------------------------------------------ users

    public async Task<PagedResult<AdminUserView>> SearchUsersAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        (int currentPage, int size) = Paging(page, pageSize);

        IQueryable<User> users = context.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // NormalizedEmail is stored upper-cased; DisplayName relies on the database's
            // case-insensitive collation, which is what every other search here does.
            string upper = search.Trim().ToUpperInvariant();
            string term = search.Trim();
            users = users.Where(user =>
                user.NormalizedEmail.Contains(upper) || user.DisplayName.Contains(term));
        }

        int totalCount = await users.CountAsync(cancellationToken);

        List<AdminUserView> items = await users
            .OrderByDescending(user => user.CreatedAtUtc)
            .ThenBy(user => user.Id)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .Select(user => new AdminUserView(
                user.Id,
                user.DisplayName,
                user.Email,
                user.Status.ToString(),
                user.EmailVerified,
                user.UserRoles.Select(assignment => assignment.Role!.Name).ToList(),
                context.Entitlements.Count(grant => grant.UserId == user.Id),
                user.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminUserView>(
            items, currentPage, size, totalCount, TotalPages(totalCount, size));
    }

    public async Task<OperationResult<AdminUserView>> SetAdminRoleAsync(
        Guid actorUserId,
        Guid userId,
        SetAdminRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (userId == actorUserId)
        {
            return OperationResult.Conflict(
                ErrorCodes.ValidationFailed,
                "You cannot change your own Admin role — ask another administrator.")
                .ToFailure<AdminUserView>();
        }

        OperationResult? invalidReason = RequireReason(request.Reason);

        if (invalidReason is not null)
        {
            return invalidReason.ToFailure<AdminUserView>();
        }

        User? user = await context.Users
            .Include(candidate => candidate.UserRoles)
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return OperationResult.NotFound().ToFailure<AdminUserView>();
        }

        Role adminRole = await context.Roles
            .SingleAsync(role => role.Name == ApplicationRoles.Admin, cancellationToken);

        bool hasRole = user.UserRoles.Any(assignment => assignment.RoleId == adminRole.Id);

        if (request.IsAdmin && !hasRole)
        {
            context.UserRoles.Add(new UserRole { UserId = userId, RoleId = adminRole.Id });
        }
        else if (!request.IsAdmin && hasRole)
        {
            context.UserRoles.RemoveRange(
                user.UserRoles.Where(assignment => assignment.RoleId == adminRole.Id));
        }

        audit.Append(
            request.IsAdmin ? "Identity.Role.AdminGranted" : "Identity.Role.AdminRemoved",
            nameof(User),
            userId,
            reason: request.Reason);

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.FromValue(await ViewAsync(userId, cancellationToken));
    }

    public async Task<OperationResult<AdminUserView>> SetUserStatusAsync(
        Guid actorUserId,
        Guid userId,
        SetUserStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (userId == actorUserId)
        {
            return OperationResult.Conflict(
                ErrorCodes.ValidationFailed,
                "You cannot disable your own account.").ToFailure<AdminUserView>();
        }

        if (!Enum.TryParse(request.TargetStatus, ignoreCase: true, out UserStatus target))
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed, "targetStatus", "Choose Active or Disabled.")
                .ToFailure<AdminUserView>();
        }

        OperationResult? invalidReason = RequireReason(request.Reason);

        if (invalidReason is not null)
        {
            return invalidReason.ToFailure<AdminUserView>();
        }

        User? user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return OperationResult.NotFound().ToFailure<AdminUserView>();
        }

        user.Status = target;
        user.UpdatedAtUtc = timeProvider.GetUtcNow();

        audit.Append(
            target == UserStatus.Disabled
                ? "Identity.User.Disabled"
                : "Identity.User.Reactivated",
            nameof(User),
            userId,
            reason: request.Reason);

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.FromValue(await ViewAsync(userId, cancellationToken));
    }

    public async Task<OperationResult<AdminUserView>> GrantCourseAsync(
        Guid userId,
        GrantCourseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult? invalidReason = RequireReason(request.Reason);

        if (invalidReason is not null)
        {
            return invalidReason.ToFailure<AdminUserView>();
        }

        if (!await context.Users.AnyAsync(user => user.Id == userId, cancellationToken))
        {
            return OperationResult.NotFound().ToFailure<AdminUserView>();
        }

        if (!await context.Courses.AnyAsync(
                course => course.Id == request.CourseId, cancellationToken))
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed, "courseId", "Choose an existing course.")
                .ToFailure<AdminUserView>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        bool alreadyHeld = await context.Entitlements.AnyAsync(
            grant => grant.UserId == userId
                && grant.Scope == EntitlementScope.Course
                && grant.CourseId == request.CourseId
                && grant.Status == EntitlementStatus.Active
                && (grant.EndsAtUtc == null || grant.EndsAtUtc > now),
            cancellationToken);

        if (alreadyHeld)
        {
            return OperationResult.Conflict(
                ErrorCodes.DuplicateValue,
                "This member already holds that course.").ToFailure<AdminUserView>();
        }

        context.Entitlements.Add(new Entitlement
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Scope = EntitlementScope.Course,
            Source = EntitlementSource.Manual,
            CourseId = request.CourseId,
            Status = EntitlementStatus.Active,
            StartsAtUtc = now,
            EndsAtUtc = null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        audit.Append(
            "Commerce.Entitlement.ManualGrant",
            nameof(User),
            userId,
            reason: request.Reason,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["courseId"] = request.CourseId.ToString("D"),
            });

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.FromValue(await ViewAsync(userId, cancellationToken));
    }

    // ------------------------------------------------------------------ records

    public async Task<PagedResult<AdminCertificateView>> ListCertificatesAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        (int currentPage, int size) = Paging(page, pageSize);

        IQueryable<Domain.Learning.Certificate> certificates =
            context.Certificates.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            certificates = certificates.Where(certificate =>
                certificate.HolderNameAtIssue.Contains(term)
                || certificate.CourseTitleAtIssue.Contains(term)
                || certificate.VerificationCode.Contains(term));
        }

        int totalCount = await certificates.CountAsync(cancellationToken);

        List<AdminCertificateView> items = await certificates
            .OrderByDescending(certificate => certificate.IssuedAtUtc)
            .ThenBy(certificate => certificate.Id)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .Select(certificate => new AdminCertificateView(
                certificate.Id,
                certificate.HolderNameAtIssue,
                certificate.CourseTitleAtIssue,
                certificate.VerificationCode,
                certificate.IssuedAtUtc,
                certificate.RevokedAtUtc,
                certificate.RevocationReason))
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminCertificateView>(
            items, currentPage, size, totalCount, TotalPages(totalCount, size));
    }

    public async Task<PagedResult<AdminOrderView>> ListOrdersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        (int currentPage, int size) = Paging(page, pageSize);

        int totalCount = await context.Orders.CountAsync(cancellationToken);

        List<AdminOrderView> items = await context.Orders
            .AsNoTracking()
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenBy(order => order.Id)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .Select(order => new AdminOrderView(
                order.Id,
                context.Users
                    .Where(user => user.Id == order.UserId)
                    .Select(user => user.Email)
                    .FirstOrDefault() ?? "(deleted account)",
                order.Items.Select(item => item.OfferName).FirstOrDefault() ?? "Purchase",
                order.Status.ToString(),
                order.TotalMinor,
                order.Currency,
                order.CreatedAtUtc,
                order.PaidAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminOrderView>(
            items, currentPage, size, totalCount, TotalPages(totalCount, size));
    }

    public async Task<PagedResult<AdminWebhookEventView>> ListWebhookEventsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        (int currentPage, int size) = Paging(page, pageSize);

        int totalCount = await context.WebhookEvents.CountAsync(cancellationToken);

        List<AdminWebhookEventView> items = await context.WebhookEvents
            .AsNoTracking()
            .OrderByDescending(webhookEvent => webhookEvent.ReceivedAtUtc)
            .ThenBy(webhookEvent => webhookEvent.Id)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .Select(webhookEvent => new AdminWebhookEventView(
                webhookEvent.Id,
                webhookEvent.Provider,
                webhookEvent.EventType,
                webhookEvent.Status.ToString(),
                webhookEvent.ReceivedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminWebhookEventView>(
            items, currentPage, size, totalCount, TotalPages(totalCount, size));
    }

    public async Task<PagedResult<AdminAuditEntryView>> ListAuditAsync(
        string? action,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        (int currentPage, int size) = Paging(page, pageSize);

        IQueryable<Domain.Auditing.AuditLog> entries = context.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(action))
        {
            string prefix = action.Trim();
            entries = entries.Where(entry => entry.Action.StartsWith(prefix));
        }

        int totalCount = await entries.CountAsync(cancellationToken);

        List<AdminAuditEntryView> items = await entries
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenBy(entry => entry.Id)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .Select(entry => new AdminAuditEntryView(
                entry.Id,
                entry.Action,
                entry.TargetType,
                entry.TargetId,
                entry.ActorUserId == null
                    ? "System"
                    : context.Users
                        .Where(user => user.Id == entry.ActorUserId)
                        .Select(user => user.DisplayName)
                        .FirstOrDefault() ?? "Former account",
                entry.Reason,
                entry.MetadataJson,
                entry.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminAuditEntryView>(
            items, currentPage, size, totalCount, TotalPages(totalCount, size));
    }

    // ------------------------------------------------------------------ flags & ops

    public async Task<IReadOnlyList<FeatureFlagView>> ListFlagsAsync(
        CancellationToken cancellationToken = default)
    {
        var stored = await context.FeatureFlags
            .AsNoTracking()
            .ToDictionaryAsync(flag => flag.Key, cancellationToken);

        return [.. KnownFlags
            .OrderBy(known => known.Key, StringComparer.Ordinal)
            .Select(known => stored.TryGetValue(known.Key, out Domain.Platform.FeatureFlag? flag)
                ? new FeatureFlagView(flag.Key, flag.Enabled, known.Value, flag.UpdatedAtUtc)

                // No row means the built-in default: on.
                : new FeatureFlagView(known.Key, true, known.Value, DateTimeOffset.MinValue))];
    }

    public async Task<OperationResult<FeatureFlagView>> SetFlagAsync(
        string key,
        SetFeatureFlagRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!KnownFlags.TryGetValue(key, out string? description))
        {
            return OperationResult.NotFound().ToFailure<FeatureFlagView>();
        }

        OperationResult? invalidReason = RequireReason(request.Reason);

        if (invalidReason is not null)
        {
            return invalidReason.ToFailure<FeatureFlagView>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        Domain.Platform.FeatureFlag? flag = await context.FeatureFlags
            .FirstOrDefaultAsync(candidate => candidate.Key == key, cancellationToken);

        if (flag is null)
        {
            flag = new Domain.Platform.FeatureFlag
            {
                Key = key,
                Description = description,
                CreatedAtUtc = now,
            };
            context.FeatureFlags.Add(flag);
        }

        flag.Enabled = request.Enabled;
        flag.Description = description;
        flag.UpdatedAtUtc = now;

        audit.Append(
            request.Enabled ? "Platform.Flag.Enabled" : "Platform.Flag.Disabled",
            nameof(Domain.Platform.FeatureFlag),
            Guid.Empty,
            reason: request.Reason,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["key"] = key });

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.FromValue(
            new FeatureFlagView(flag.Key, flag.Enabled, description, flag.UpdatedAtUtc));
    }

    public async Task<OpsSnapshot> GetOpsSnapshotAsync(CancellationToken cancellationToken = default)
    {
        bool reachable;
        string lastMigration = "(unknown)";
        int pendingCount = 0;

        try
        {
            IEnumerable<string> applied =
                await context.Database.GetAppliedMigrationsAsync(cancellationToken);
            IEnumerable<string> pending =
                await context.Database.GetPendingMigrationsAsync(cancellationToken);

            lastMigration = applied.LastOrDefault() ?? "(none)";
            pendingCount = pending.Count();
            reachable = true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            reachable = false;
        }

        return new OpsSnapshot(
            environment.EnvironmentName,
            Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion,
            lastMigration,
            pendingCount,
            mediaOptions.Mode.ToString(),
            videoOptions.Mode.ToString(),
            paymentOptions.Mode.ToString(),
            reachable);
    }

    // ------------------------------------------------------------------ helpers

    private static OperationResult? RequireReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "reason",
                "Give a reason; it is recorded in the audit trail.")
            : null;

    private async Task<AdminUserView> ViewAsync(Guid userId, CancellationToken cancellationToken) =>
        await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new AdminUserView(
                user.Id,
                user.DisplayName,
                user.Email,
                user.Status.ToString(),
                user.EmailVerified,
                user.UserRoles.Select(assignment => assignment.Role!.Name).ToList(),
                context.Entitlements.Count(grant => grant.UserId == user.Id),
                user.CreatedAtUtc))
            .SingleAsync(cancellationToken);

    private static (int Page, int PageSize) Paging(int page, int pageSize) => (
        page < 1 ? 1 : page,
        pageSize < 1 ? 20 : Math.Min(pageSize, MaxPageSize));

    private static int TotalPages(int totalCount, int size) =>
        size == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)size);
}
