using System.Net.Http.Headers;
using System.Text;
using Azure.Identity;
using Azure.Storage.Blobs;
using DanielsDojo.Application.Media;
using DanielsDojo.Domain.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DanielsDojo.Infrastructure.Media;

/// <summary>Registers the media providers named by configuration.</summary>
/// <remarks>
/// <para>
/// The mode is read from configuration and acted on literally. Nothing here inspects whether a
/// credential happens to be present and quietly picks an adapter to suit: a real deployment
/// that is missing its account name fails at startup, because the alternative — silently
/// falling back to the in-memory adapter — would look like a working upload and lose the only
/// copy of a master.
/// </para>
/// <para>
/// Options are validated on start for the same reason. A misconfiguration should stop a deploy,
/// not surface as a failed upload three days later.
/// </para>
/// </remarks>
public static class MediaRegistration
{
    /// <summary>Named client used for the video provider's REST API.</summary>
    public const string VideoHttpClientName = "media-video";

    /// <summary>Registers storage and video adapters, and the services built on them.</summary>
    public static IServiceCollection AddMedia(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MediaStorageOptions>()
            .Bind(configuration.GetSection(MediaStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                static options => options.Mode != ProviderMode.Real
                    || !string.IsNullOrWhiteSpace(options.AccountName),
                $"{MediaStorageOptions.SectionName}:AccountName is required when Mode is Real.")
            .ValidateOnStart();

        services.AddOptions<VideoProviderOptions>()
            .Bind(configuration.GetSection(VideoProviderOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                static options => options.Mode != ProviderMode.Real
                    || (!string.IsNullOrWhiteSpace(options.TokenId)
                        && !string.IsNullOrWhiteSpace(options.TokenSecret)),
                $"{VideoProviderOptions.SectionName}:TokenId and :TokenSecret are required when Mode is Real.")
            .Validate(
                static options => options.Mode != ProviderMode.Real
                    || !string.IsNullOrWhiteSpace(options.WebhookSecret),
                $"{VideoProviderOptions.SectionName}:WebhookSecret is required when Mode is Real; "
                + "without it no inbound notification can be authenticated.")
            .Validate(
                static options => options.Mode != ProviderMode.Real
                    || (!string.IsNullOrWhiteSpace(options.SigningKeyId)
                        && !string.IsNullOrWhiteSpace(options.SigningKeyBase64)),
                $"{VideoProviderOptions.SectionName}:SigningKeyId and :SigningKeyBase64 are required "
                + "when Mode is Real; playback is always signed.")
            .ValidateOnStart();

        ProviderMode storageMode = ReadMode(configuration, MediaStorageOptions.SectionName);
        ProviderMode videoMode = ReadMode(configuration, VideoProviderOptions.SectionName);

        AddStorage(services, storageMode);
        AddVideo(services, videoMode);

        services.AddScoped<IAdminMediaService, AdminMediaService>();
        services.AddScoped<IMediaWebhookService, MediaWebhookService>();
        services.AddScoped<ILessonPlaybackService, LessonPlaybackService>();

        return services;
    }

    private static void AddStorage(IServiceCollection services, ProviderMode mode)
    {
        switch (mode)
        {
            case ProviderMode.Real:
                services.AddSingleton(provider =>
                {
                    MediaStorageOptions options = provider
                        .GetRequiredService<IOptions<MediaStorageOptions>>().Value;

                    // Managed identity in Azure, developer sign-in locally. No account key is
                    // read, held, or logged anywhere in this process.
                    return new BlobServiceClient(
                        new Uri($"https://{options.AccountName}.blob.core.windows.net"),
                        new DefaultAzureCredential());
                });

                services.AddScoped<IMediaStorage, AzureBlobMediaStorage>();
                break;

            case ProviderMode.Deterministic:
                services.AddSingleton<DeterministicMediaStore>();
                services.AddScoped<IMediaStorage, DeterministicMediaStorage>();
                break;

            case ProviderMode.Disabled:
            default:
                services.AddScoped<IMediaStorage, DisabledMediaStorage>();
                break;
        }
    }

    private static void AddVideo(IServiceCollection services, ProviderMode mode)
    {
        switch (mode)
        {
            case ProviderMode.Real:
                services.AddHttpClient<IVideoPipeline, MuxVideoPipeline>(VideoHttpClientName, (provider, client) =>
                {
                    VideoProviderOptions options = provider
                        .GetRequiredService<IOptions<VideoProviderOptions>>().Value;

                    client.BaseAddress = options.BaseAddress;
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                        "Basic",
                        Convert.ToBase64String(
                            Encoding.UTF8.GetBytes($"{options.TokenId}:{options.TokenSecret}")));
                });
                break;

            case ProviderMode.Deterministic:
                services.AddSingleton<DeterministicVideoPipeline>();
                services.AddScoped<IVideoPipeline>(provider =>
                    provider.GetRequiredService<DeterministicVideoPipeline>());
                break;

            case ProviderMode.Disabled:
            default:
                services.AddScoped<IVideoPipeline, DisabledVideoPipeline>();
                break;
        }
    }

    /// <summary>
    /// Reads a provider mode. An unrecognised value is a configuration error rather than a
    /// reason to guess, because every wrong guess here is either an outage or a data loss.
    /// </summary>
    private static ProviderMode ReadMode(IConfiguration configuration, string sectionName)
    {
        string? configured = configuration.GetSection(sectionName)["Mode"];

        if (string.IsNullOrWhiteSpace(configured))
        {
            return ProviderMode.Disabled;
        }

        return Enum.TryParse(configured, ignoreCase: false, out ProviderMode mode)
            ? mode
            : throw new InvalidOperationException(
                $"{sectionName}:Mode must be one of Disabled, Deterministic, or Real. "
                + "The value is compared exactly, so casing matters.");
    }
}
