using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DanielsDojo.Application.Media;
using DanielsDojo.Domain.Media;
using Microsoft.Extensions.Options;

namespace DanielsDojo.Infrastructure.Media;

/// <summary>
/// Video processing through Mux's REST API.
/// </summary>
/// <remarks>
/// <para>
/// Mux is asked to pull the master from storage with a short-lived read authorisation, so the
/// original travels browser to Azure to Mux and never through this process. The read
/// authorisation is passed as a parameter and is never logged or persisted, because it grants
/// read access to the master for as long as it lives.
/// </para>
/// <para>
/// Playback is signed. An unsigned playback identifier is a public URL that works for anyone
/// who ever sees it, which would make paid course video freely shareable.
/// </para>
/// </remarks>
public sealed class MuxVideoPipeline(
    HttpClient client,
    IOptions<VideoProviderOptions> options,
    TimeProvider timeProvider) : IVideoPipeline
{
    private readonly VideoProviderOptions _options = options.Value;

    /// <inheritdoc />
    public ProviderMode Mode => ProviderMode.Real;

    /// <inheritdoc />
    public bool UsesSignedPlayback => true;

    /// <inheritdoc />
    public async Task<VideoIngestTicket> StartIngestAsync(
        Uri sourceReadUri,
        string correlationKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceReadUri);

        var request = new
        {
            inputs = new[] { new { url = sourceReadUri.ToString() } },
            playback_policies = new[] { "signed" },
            video_quality = "basic",

            // Echoed back on every notification about this asset, which is how a delivery that
            // arrives before the asset identifier is recorded still finds its lesson.
            passthrough = correlationKey,
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "video/v1/assets",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        VideoAssetState? state = await ReadDataAsync(response, cancellationToken);

        return new VideoIngestTicket(
            state?.AssetId,
            correlationKey,
            state?.Status ?? Domain.Catalog.LessonVideoStatus.MuxIngesting);
    }

    /// <inheritdoc />
    public async Task<VideoAssetState?> GetAssetAsync(
        string assetId,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await client.GetAsync(
            new Uri($"video/v1/assets/{Uri.EscapeDataString(assetId)}", UriKind.Relative),
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // The provider no longer knows this asset. Reconciliation reports that rather than
            // pretending the lesson is fine.
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await ReadDataAsync(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<VideoCaptionTicket> AddCaptionTrackAsync(
        string assetId,
        Uri captionReadUri,
        string languageCode,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captionReadUri);

        var request = new
        {
            url = captionReadUri.ToString(),
            type = "text",
            text_type = "subtitles",
            language_code = languageCode,
            name = displayName,
            closed_captions = true,
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri($"video/v1/assets/{Uri.EscapeDataString(assetId)}/tracks", UriKind.Relative),
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        using JsonDocument document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        string trackId = document.RootElement.TryGetProperty("data", out JsonElement data)
            && data.TryGetProperty("id", out JsonElement id)
                ? id.GetString() ?? string.Empty
                : string.Empty;

        return new VideoCaptionTicket(trackId, Domain.Catalog.LessonVideoStatus.Processing);
    }

    /// <inheritdoc />
    public VideoProviderEvent? VerifyEvent(string payload, string? signatureHeader, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            // No secret configured means no delivery can be trusted. Failing closed here is the
            // difference between an unauthenticated endpoint and a rejected request.
            return null;
        }

        return ProviderSignatures.IsValidWebhookSignature(
            payload,
            signatureHeader,
            _options.WebhookSecret,
            now,
            TimeSpan.FromSeconds(_options.WebhookToleranceSeconds))
            ? MuxEventParser.Parse(payload)
            : null;
    }

    /// <inheritdoc />
    public string CreatePlaybackToken(string playbackId, DateTimeOffset expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(_options.SigningKeyId)
            || string.IsNullOrWhiteSpace(_options.SigningKeyBase64))
        {
            throw new InvalidOperationException(
                "Signed playback is configured but no signing key is present. Set "
                + $"{VideoProviderOptions.SectionName}:SigningKeyId and :SigningKeyBase64.");
        }

        using RSA key = RSA.Create();
        key.ImportFromPem(
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(_options.SigningKeyBase64)));

        return ProviderSignatures.CreateRsaPlaybackToken(
            key,
            _options.SigningKeyId,
            playbackId,
            expiresAtUtc);
    }

    /// <summary>The current time, exposed so callers share this adapter's clock.</summary>
    internal DateTimeOffset Now => timeProvider.GetUtcNow();

    private static async Task<VideoAssetState?> ReadDataAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        return document.RootElement.TryGetProperty("data", out JsonElement data)
            ? MuxEventParser.ReadAssetState(data)
            : null;
    }
}
