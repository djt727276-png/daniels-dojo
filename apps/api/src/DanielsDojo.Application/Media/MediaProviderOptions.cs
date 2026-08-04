using System.ComponentModel.DataAnnotations;
using DanielsDojo.Domain.Media;

namespace DanielsDojo.Application.Media;

/// <summary>Configuration for exact-source blob storage.</summary>
/// <remarks>
/// <para>
/// The mode is explicit and is never inferred from whether a key happens to be present. A
/// missing account name must fail loudly rather than silently degrade a production deployment
/// into the deterministic adapter, because the deterministic adapter does not store anything
/// durably — a silent downgrade would look like a working upload and lose the master.
/// </para>
/// </remarks>
public sealed class MediaStorageOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Media:Storage";

    /// <summary>Which adapter serves storage.</summary>
    public ProviderMode Mode { get; set; } = ProviderMode.Disabled;

    /// <summary>Storage account name, required in <see cref="ProviderMode.Real"/>.</summary>
    public string? AccountName { get; set; }

    /// <summary>Container holding original uploaded masters.</summary>
    [Required]
    [MaxLength(63)]
    public string SourceContainer { get; set; } = "media-source";

    /// <summary>How long an upload authorisation stays valid.</summary>
    [Range(1, 720)]
    public int UploadWindowMinutes { get; set; } = 120;

    /// <summary>
    /// How long the read authorisation handed to the video provider stays valid. Deliberately
    /// short: it exists only for the provider to pull the master once.
    /// </summary>
    [Range(1, 1440)]
    public int IngestReadWindowMinutes { get; set; } = 60;

    /// <summary>Largest upload the server will authorise, in bytes.</summary>
    [Range(1, 64L * 1024 * 1024 * 1024)]
    public long MaxUploadBytes { get; set; } = 16L * 1024 * 1024 * 1024;

    /// <summary>
    /// Bytes downloaded during restore verification before the check is considered proven.
    /// A full re-download of a multi-gigabyte master would cost more than it proves; reading
    /// the head of the object confirms the object is genuinely readable and returns the length
    /// the service reports for it.
    /// </summary>
    [Range(1024, 64 * 1024 * 1024)]
    public int RestoreProbeBytes { get; set; } = 1024 * 1024;
}

/// <summary>Configuration for the video processing provider.</summary>
public sealed class VideoProviderOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Media:Video";

    /// <summary>Which adapter serves video processing.</summary>
    public ProviderMode Mode { get; set; } = ProviderMode.Disabled;

    /// <summary>API base address.</summary>
    public Uri BaseAddress { get; set; } = new("https://api.mux.com/");

    /// <summary>API token identifier, required in <see cref="ProviderMode.Real"/>.</summary>
    public string? TokenId { get; set; }

    /// <summary>API token secret, required in <see cref="ProviderMode.Real"/>.</summary>
    public string? TokenSecret { get; set; }

    /// <summary>Signing key identifier used to mint playback tokens.</summary>
    public string? SigningKeyId { get; set; }

    /// <summary>Base64 PEM private key matching <see cref="SigningKeyId"/>.</summary>
    public string? SigningKeyBase64 { get; set; }

    /// <summary>Shared secret used to authenticate inbound webhooks.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>How long an issued playback token stays valid.</summary>
    [Range(1, 1440)]
    public int PlaybackTokenMinutes { get; set; } = 60;

    /// <summary>
    /// How old an inbound webhook signature may be before it is rejected, in seconds. This is
    /// what stops a captured request being replayed later.
    /// </summary>
    [Range(30, 3600)]
    public int WebhookToleranceSeconds { get; set; } = 300;
}
