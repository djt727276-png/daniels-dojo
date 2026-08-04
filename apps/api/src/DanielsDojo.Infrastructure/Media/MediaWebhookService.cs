using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DanielsDojo.Application.Common;
using DanielsDojo.Application.Media;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Domain.Media;
using DanielsDojo.Infrastructure.Auditing;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DanielsDojo.Infrastructure.Media;

/// <summary>
/// Applies inbound provider notifications.
/// </summary>
/// <remarks>
/// <para>
/// Three things have to hold before a notification changes anything. It must be signed, so an
/// anonymous endpoint cannot be used to take somebody's lesson off the air. It must not have
/// been seen before, because providers redeliver. And it must be newer than the last one
/// applied to that video, because deliveries overtake one another and a stale "processing"
/// arriving after "ready" would undo a working lesson.
/// </para>
/// <para>
/// A rejected signature returns false and writes nothing — no audit row, no log line carrying
/// the payload — so an unauthenticated caller cannot fill the trail with noise. Only a hash of
/// the payload is stored, never the payload itself.
/// </para>
/// </remarks>
internal sealed class MediaWebhookService : IMediaWebhookService
{
    /// <summary>Provider name recorded against inbound events.</summary>
    private const string WebhookProvider = "Mux";

    private readonly DanielsDojoDbContext context;
    private readonly IVideoPipeline video;
    private readonly VideoStateMachine stateMachine;
    private readonly TimeProvider timeProvider;
    private readonly AuditTrail audit;

    public MediaWebhookService(
        DanielsDojoDbContext context,
        IVideoPipeline video,
        IOperationContext operationContext,
        TimeProvider timeProvider)
    {
        this.context = context;
        this.video = video;
        this.timeProvider = timeProvider;

        stateMachine = new VideoStateMachine(context);
        audit = new AuditTrail(context, operationContext, timeProvider);
    }

    public async Task<bool> HandleVideoEventAsync(
        string payload,
        string? signatureHeader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        DateTimeOffset now = timeProvider.GetUtcNow();

        if (video.VerifyEvent(payload, signatureHeader, now) is not { } notification)
        {
            return false;
        }

        // Recorded before anything is applied, so a redelivery of an event that failed halfway
        // through is still recognised as the same event.
        bool alreadySeen = await context.WebhookEvents
            .AnyAsync(
                seen => seen.Provider == WebhookProvider && seen.ExternalEventId == notification.EventId,
                cancellationToken);

        if (alreadySeen)
        {
            return true;
        }

        var record = new WebhookEvent
        {
            Id = Guid.CreateVersion7(),
            Provider = WebhookProvider,
            ExternalEventId = notification.EventId,
            EventType = notification.EventType,
            Status = WebhookEventStatus.Received,
            AttemptCount = 1,
            ReceivedAtUtc = now,
            PayloadSha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant(),
        };

        context.WebhookEvents.Add(record);

        LessonVideo? target = await FindTargetAsync(notification, cancellationToken);

        if (target is null || notification.State is null)
        {
            record.Status = WebhookEventStatus.Ignored;
            record.ProcessedAtUtc = now;

            await context.SaveChangesAsync(cancellationToken);

            return true;
        }

        if (!MediaLifecycle.IsNewerThan(notification.OccurredAtUtc, target.LastProviderEventAtUtc))
        {
            // Out of order. Understood and deliberately not applied, which is a success as far
            // as the provider is concerned — retrying would not change the answer.
            record.Status = WebhookEventStatus.Ignored;
            record.ProcessedAtUtc = now;

            audit.Append(
                "Media.Webhook.Stale",
                nameof(LessonVideo),
                target.Id,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["eventType"] = notification.EventType,
                });

            await context.SaveChangesAsync(cancellationToken);

            return true;
        }

        bool changed = stateMachine.Apply(target, notification.State, notification.OccurredAtUtc);

        record.Status = WebhookEventStatus.Processed;
        record.ProcessedAtUtc = now;

        audit.Append(
            "Media.Webhook.Applied",
            nameof(LessonVideo),
            target.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["eventType"] = notification.EventType,
                ["status"] = target.Status.ToString(),
                ["changed"] = changed ? "true" : "false",
                ["occurredAtUtc"] = notification.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture),
            });

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Finds the lesson a notification concerns: by asset first, then by the correlation key the
    /// ingest carried — which is the only handle available when the asset identifier has not
    /// been recorded yet.
    /// </summary>
    private async Task<LessonVideo?> FindTargetAsync(
        VideoProviderEvent notification,
        CancellationToken cancellationToken)
    {
        if (notification.AssetId is { Length: > 0 } assetId)
        {
            LessonVideo? byAsset = await context.LessonVideos
                .FirstOrDefaultAsync(record => record.MuxAssetId == assetId, cancellationToken);

            if (byAsset is not null)
            {
                return byAsset;
            }
        }

        if (notification.UploadId is { Length: > 0 } uploadId)
        {
            return await context.LessonVideos
                .FirstOrDefaultAsync(record => record.MuxUploadId == uploadId, cancellationToken);
        }

        return null;
    }
}
