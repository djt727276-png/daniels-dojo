using DanielsDojo.Api.Authentication;
using DanielsDojo.Api.Common;
using DanielsDojo.Application.Admin;
using DanielsDojo.Application.Identity;

namespace DanielsDojo.Api.Admin;

/// <summary>
/// The operator's back office: accounts, records, switches, and what is running.
/// </summary>
/// <remarks>
/// Everything requires the database-backed Admin role; every mutation takes a reason the
/// service records. The self-protection rules live in the service, so no route can be used
/// to remove the caller's own access.
/// </remarks>
internal static class AdminOperationsEndpoints
{
    /// <summary>Maps the back-office routes.</summary>
    public static void MapAdminOperationsEndpoints(this RouteGroupBuilder apiV1)
    {
        RouteGroupBuilder admin = apiV1
            .MapGroup("/admin")
            .RequireAuthorization(AuthenticationRegistration.AdminPolicy);

        admin.MapGet("/users", async (
                IAdminOperationsService service,
                CancellationToken cancellationToken,
                string? search = null,
                int page = 1,
                int pageSize = 20) =>
            TypedResults.Ok(
                await service.SearchUsersAsync(search, page, pageSize, cancellationToken)))
            .WithName("SearchUsers");

        admin.MapPost("/users/{userId:guid}/admin-role", async (
                Guid userId,
                SetAdminRoleRequest request,
                ICurrentUser currentUser,
                IAdminOperationsService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(await service.SetAdminRoleAsync(
                currentUser.User!.UserId, userId, request, cancellationToken)))
            .WithName("SetAdminRole");

        admin.MapPost("/users/{userId:guid}/status", async (
                Guid userId,
                SetUserStatusRequest request,
                ICurrentUser currentUser,
                IAdminOperationsService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(await service.SetUserStatusAsync(
                currentUser.User!.UserId, userId, request, cancellationToken)))
            .WithName("SetUserStatus");

        admin.MapPost("/users/{userId:guid}/grants", async (
                Guid userId,
                GrantCourseRequest request,
                IAdminOperationsService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.GrantCourseAsync(userId, request, cancellationToken)))
            .WithName("GrantCourse");

        admin.MapGet("/certificates", async (
                IAdminOperationsService service,
                CancellationToken cancellationToken,
                string? search = null,
                int page = 1,
                int pageSize = 20) =>
            TypedResults.Ok(
                await service.ListCertificatesAsync(search, page, pageSize, cancellationToken)))
            .WithName("ListAdminCertificates");

        admin.MapGet("/orders", async (
                IAdminOperationsService service,
                CancellationToken cancellationToken,
                int page = 1,
                int pageSize = 20) =>
            TypedResults.Ok(await service.ListOrdersAsync(page, pageSize, cancellationToken)))
            .WithName("ListAdminOrders");

        admin.MapGet("/webhook-events", async (
                IAdminOperationsService service,
                CancellationToken cancellationToken,
                int page = 1,
                int pageSize = 20) =>
            TypedResults.Ok(
                await service.ListWebhookEventsAsync(page, pageSize, cancellationToken)))
            .WithName("ListAdminWebhookEvents");

        admin.MapGet("/audit", async (
                IAdminOperationsService service,
                CancellationToken cancellationToken,
                string? action = null,
                int page = 1,
                int pageSize = 20) =>
            TypedResults.Ok(
                await service.ListAuditAsync(action, page, pageSize, cancellationToken)))
            .WithName("ListAuditEntries");

        admin.MapGet("/flags", async (
                IAdminOperationsService service,
                CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.ListFlagsAsync(cancellationToken)))
            .WithName("ListFeatureFlags");

        admin.MapPut("/flags/{key}", async (
                string key,
                SetFeatureFlagRequest request,
                IAdminOperationsService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.SetFlagAsync(key, request, cancellationToken)))
            .WithName("SetFeatureFlag");

        admin.MapGet("/ops", async (
                IAdminOperationsService service,
                CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetOpsSnapshotAsync(cancellationToken)))
            .WithName("GetOpsSnapshot");
    }
}
