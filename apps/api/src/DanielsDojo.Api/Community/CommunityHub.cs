using DanielsDojo.Api.Authentication;
using DanielsDojo.Application.Community;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DanielsDojo.Api.Community;

/// <summary>What the server pushes to a connected member.</summary>
public interface ICommunityClient
{
    /// <summary>A direct message arrived in one of the member's conversations.</summary>
    Task MessageReceived(Guid conversationId);

    /// <summary>The member's unread counters changed; refetch them.</summary>
    Task UnreadChanged();
}

/// <summary>
/// The live channel for community events.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately receive-only: clients never send content through the hub. Messages are
/// written over the audited, rate-limited REST surface, and this connection only tells the
/// other side to go and fetch — so the hub can leak nothing that REST would not serve, and
/// there is no second write path to secure.
/// </para>
/// <para>
/// Identity is resolved from the validated token's immutable (tenant, subject) pair against
/// the local user table — the same ownership key every HTTP request uses — rather than from
/// request-scoped accessors, because hub dispatch does not share the HTTP request's service
/// scope across every transport. A connection is subscribed to its own user group and
/// nothing else, so there is no conversation identifier a client could guess its way into.
/// </para>
/// </remarks>
[Authorize(Policy = AuthenticationRegistration.StudentPolicy)]
public sealed class CommunityHub(DanielsDojoDbContext context) : Hub<ICommunityClient>
{
    /// <summary>Group name for one member's connections.</summary>
    internal static string UserGroup(Guid userId) => $"user:{userId:D}";

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        string? tenantId = Context.User?.FindFirst("tid")?.Value;
        string? objectId = Context.User?.FindFirst("oid")?.Value
            ?? Context.User?.FindFirst(
                "http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(objectId))
        {
            // Authorization already passed, so this is a malformed principal, not a customer.
            Context.Abort();
            return;
        }

        Guid? userId = await context.Users
            .AsNoTracking()
            .Where(user => user.ExternalIssuer == tenantId && user.ExternalSubjectId == objectId)
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(Context.ConnectionAborted);

        if (userId is not { } localUserId)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(localUserId));
        await base.OnConnectedAsync();
    }
}

/// <summary>Rings connected clients through the hub.</summary>
internal sealed class SignalRRealtimeNotifier(IHubContext<CommunityHub, ICommunityClient> hub)
    : IRealtimeNotifier
{
    public Task MessageReceivedAsync(
        Guid recipientUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default) =>
        hub.Clients.Group(CommunityHub.UserGroup(recipientUserId))
            .MessageReceived(conversationId);

    public Task UnreadChangedAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken = default) =>
        hub.Clients.Group(CommunityHub.UserGroup(recipientUserId)).UnreadChanged();
}
