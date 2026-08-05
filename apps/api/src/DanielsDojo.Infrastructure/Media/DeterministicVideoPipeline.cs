using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DanielsDojo.Application.Media;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Media;
using Microsoft.Extensions.Options;

namespace DanielsDojo.Infrastructure.Media;

/// <summary>
/// A video provider that behaves like the real one without leaving the process.
/// </summary>
/// <remarks>
/// <para>
/// It deliberately does not jump straight to playable. An ingest starts in the provider's
/// processing state and only becomes ready once something asks — either a notification arrives
/// or reconciliation goes looking. That is what makes the interesting failures reproducible:
/// the webhook that never came, the duplicate that arrives twice, and the stale one that turns
/// up after a newer state was already applied.
/// </para>
/// <para>
/// Identifiers are derived from the correlation key rather than random, so the same lesson
/// always produces the same asset in a given run and a test can assert on them.
/// </para>
/// </remarks>
public sealed class DeterministicVideoPipeline(
    IOptions<VideoProviderOptions> options,
    TimeProvider timeProvider) : IVideoPipeline
{
    /// <summary>Secret the deterministic adapter signs and verifies notifications with.</summary>
    public const string DeterministicWebhookSecret = "deterministic-webhook-secret";

    /// <summary>Key identifier stamped into deterministic playback tokens.</summary>
    public const string DeterministicSigningKeyId = "deterministic-signing-key";

    private readonly ConcurrentDictionary<string, VideoAssetState> _assets = new(StringComparer.Ordinal);
    private readonly VideoProviderOptions _options = options.Value;

    /// <inheritdoc />
    public ProviderMode Mode => ProviderMode.Deterministic;

    /// <inheritdoc />
    public bool UsesSignedPlayback => true;

    /// <inheritdoc />
    public Task<VideoIngestTicket> StartIngestAsync(
        Uri sourceReadUri,
        string correlationKey,
        CancellationToken cancellationToken = default)
    {
        string assetId = Derive("asset", correlationKey);

        _assets[assetId] = new VideoAssetState(
            assetId,
            PlaybackId: null,
            LessonVideoStatus.Processing,
            DurationSeconds: null,
            AspectRatio: null,
            FailureCode: null);

        return Task.FromResult(new VideoIngestTicket(
            assetId,
            correlationKey,
            LessonVideoStatus.MuxIngesting));
    }

    /// <inheritdoc />
    public Task<VideoAssetState?> GetAssetAsync(
        string assetId,
        CancellationToken cancellationToken = default)
    {
        if (!_assets.TryGetValue(assetId, out VideoAssetState? state))
        {
            return Task.FromResult<VideoAssetState?>(null);
        }

        // Asking is what finishes it, which models a provider that completed while nobody was
        // listening — exactly the case reconciliation exists for.
        VideoAssetState ready = state with
        {
            PlaybackId = Derive("playback", assetId),
            Status = LessonVideoStatus.Ready,
            DurationSeconds = 42,
            AspectRatio = "16:9",
        };

        _assets[assetId] = ready;

        return Task.FromResult<VideoAssetState?>(ready);
    }

    /// <inheritdoc />
    public Task<VideoCaptionTicket> AddCaptionTrackAsync(
        string assetId,
        Uri captionReadUri,
        string languageCode,
        string displayName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new VideoCaptionTicket(
            Derive("track", $"{assetId}:{languageCode}"),
            LessonVideoStatus.Ready));

    /// <inheritdoc />
    public VideoProviderEvent? VerifyEvent(string payload, string? signatureHeader, DateTimeOffset now)
    {
        string secret = string.IsNullOrWhiteSpace(_options.WebhookSecret)
            ? DeterministicWebhookSecret
            : _options.WebhookSecret;

        return ProviderSignatures.IsValidWebhookSignature(
            payload,
            signatureHeader,
            secret,
            now,
            TimeSpan.FromSeconds(_options.WebhookToleranceSeconds))
            ? MuxEventParser.Parse(payload)
            : null;
    }

    /// <inheritdoc />
    public string CreatePlaybackToken(string playbackId, DateTimeOffset expiresAtUtc) =>
        ProviderSignatures.CreateHmacPlaybackToken(
            DeterministicWebhookSecret,
            DeterministicSigningKeyId,
            playbackId,
            expiresAtUtc);

    /// <summary>Builds the notification a provider would send, for the deterministic smoke.</summary>
    public (string Payload, string Signature) CreateReadyNotification(string assetId, string correlationKey)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        var body = new
        {
            id = Derive("event", $"{assetId}:ready"),
            type = "video.asset.ready",
            created_at = now.ToString("O", CultureInfo.InvariantCulture),
            data = new
            {
                id = assetId,
                status = "ready",
                duration = 42.0,
                aspect_ratio = "16:9",
                passthrough = correlationKey,
                playback_ids = new[]
                {
                    new { id = Derive("playback", assetId), policy = "signed" },
                },
            },
        };

        string payload = JsonSerializer.Serialize(body);

        string secret = string.IsNullOrWhiteSpace(_options.WebhookSecret)
            ? DeterministicWebhookSecret
            : _options.WebhookSecret;

        return (payload, ProviderSignatures.CreateWebhookSignature(payload, secret, now));
    }

    /// <summary>
    /// Stable identifiers from a prefix and a key, so the same input always names the same
    /// asset and a failing test points at one thing rather than a fresh random value.
    /// </summary>
    private static string Derive(string prefix, string key) =>
        prefix
        + "-"
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{prefix}:{key}")))[..24]
            .ToLowerInvariant();
}
