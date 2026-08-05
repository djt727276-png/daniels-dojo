namespace DanielsDojo.Application.Community;

/// <summary>
/// Pushes live events to connected members.
/// </summary>
/// <remarks>
/// <para>
/// The database is the source of truth and REST is the source of history; this interface is
/// only the doorbell. Every payload it carries is already persisted before it is pushed, so
/// a member who was offline reconciles by fetching — nothing exists only in flight.
/// </para>
/// <para>
/// It is an abstraction so the transport can move (API-hosted SignalR today, Azure SignalR
/// at scale) without touching the services that ring it, and so integration tests that do
/// not care about transport can run against the no-op default.
/// </para>
/// </remarks>
public interface IRealtimeNotifier
{
    /// <summary>Tells one member a direct message arrived in one of their conversations.</summary>
    Task MessageReceivedAsync(
        Guid recipientUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>Tells one member their unread counters changed.</summary>
    Task UnreadChangedAsync(Guid recipientUserId, CancellationToken cancellationToken = default);
}

/// <summary>The default when no realtime transport is registered: silence, not failure.</summary>
public sealed class NoopRealtimeNotifier : IRealtimeNotifier
{
    /// <inheritdoc />
    public Task MessageReceivedAsync(
        Guid recipientUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task UnreadChangedAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
