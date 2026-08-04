using System.Globalization;
using System.Text.Json;
using DanielsDojo.Application.Media;
using DanielsDojo.Domain.Catalog;

namespace DanielsDojo.Infrastructure.Media;

/// <summary>
/// Reads provider payloads into the application's own vocabulary.
/// </summary>
/// <remarks>
/// Shared by both adapters so the deterministic suite parses the same shapes the real provider
/// sends. Anything unrecognised produces null rather than an exception: a payload shape that
/// changed is an operational fact to be reported, not a crash on a public endpoint.
/// </remarks>
public static class MuxEventParser
{
    /// <summary>Maps a provider asset status onto the lesson video lifecycle.</summary>
    public static LessonVideoStatus MapStatus(string? providerStatus, bool hasPlayback) =>
        providerStatus switch
        {
            "ready" when hasPlayback => LessonVideoStatus.Ready,
            "ready" => LessonVideoStatus.Processing,
            "preparing" => LessonVideoStatus.Processing,
            "errored" => LessonVideoStatus.Failed,
            _ => LessonVideoStatus.Processing,
        };

    /// <summary>Parses a notification body, or returns null when it is not one we act on.</summary>
    public static VideoProviderEvent? Parse(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out JsonElement typeElement)
                || typeElement.GetString() is not { Length: > 0 } eventType)
            {
                return null;
            }

            string eventId = root.TryGetProperty("id", out JsonElement idElement)
                ? idElement.GetString() ?? string.Empty
                : string.Empty;

            if (eventId.Length == 0)
            {
                // Without an identifier a redelivery cannot be told apart from a new event, so
                // there is no safe way to apply it exactly once.
                return null;
            }

            DateTimeOffset occurredAt =
                root.TryGetProperty("created_at", out JsonElement createdElement)
                && DateTimeOffset.TryParse(
                    createdElement.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset parsed)
                    ? parsed
                    : DateTimeOffset.MinValue;

            if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
            {
                return new VideoProviderEvent(eventId, eventType, null, null, occurredAt, null);
            }

            VideoAssetState? state = ReadAssetState(data);

            return new VideoProviderEvent(
                eventId,
                eventType,
                state?.AssetId,
                ReadString(data, "passthrough"),
                occurredAt,
                state);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads the asset shape shared by notifications and direct asset reads.</summary>
    public static VideoAssetState? ReadAssetState(JsonElement data)
    {
        if (ReadString(data, "id") is not { Length: > 0 } assetId)
        {
            return null;
        }

        string? playbackId = null;

        if (data.TryGetProperty("playback_ids", out JsonElement playbackIds)
            && playbackIds.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement candidate in playbackIds.EnumerateArray())
            {
                if (ReadString(candidate, "id") is { Length: > 0 } id)
                {
                    playbackId = id;
                    break;
                }
            }
        }

        string? providerStatus = ReadString(data, "status");

        int? duration =
            data.TryGetProperty("duration", out JsonElement durationElement)
            && durationElement.ValueKind == JsonValueKind.Number
                ? (int)Math.Round(durationElement.GetDouble())
                : null;

        string? failureCode = null;

        if (data.TryGetProperty("errors", out JsonElement errors)
            && errors.ValueKind == JsonValueKind.Object)
        {
            failureCode = ReadString(errors, "type");
        }

        LessonVideoStatus status = MapStatus(providerStatus, playbackId is { Length: > 0 });

        if (status == LessonVideoStatus.Failed)
        {
            failureCode ??= "provider_errored";
        }

        return new VideoAssetState(
            assetId,
            playbackId,
            status,
            duration,
            ReadString(data, "aspect_ratio"),
            failureCode);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
