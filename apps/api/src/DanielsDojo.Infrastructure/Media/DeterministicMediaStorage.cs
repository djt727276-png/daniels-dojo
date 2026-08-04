using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using DanielsDojo.Application.Media;
using DanielsDojo.Domain.Media;
using Microsoft.Extensions.Options;

namespace DanielsDojo.Infrastructure.Media;

/// <summary>
/// The bytes a deterministic upload wrote, and what the fake service reports about them.
/// </summary>
/// <remarks>
/// There is no content hash here because Azure does not produce one either: a blob uploaded in
/// blocks — which is every real master — comes back with no <c>Content-MD5</c>. Inventing one
/// would make the deterministic adapter easier to satisfy than the real service, and the whole
/// point of it is that it is not.
/// </remarks>
public sealed record DeterministicObject(
    byte[] Content,
    string ContentType,
    string ETag,
    string VersionId);

/// <summary>
/// In-process object storage standing in for the real service.
/// </summary>
/// <remarks>
/// <para>
/// This exists so the entire upload path — authorise, write, read back, hash, compare — can be
/// exercised end to end with no network and no credentials, including from the browser during
/// development. It is a genuine store rather than a set of canned responses: the bytes the
/// browser sends are the bytes that come back, so a defect in the verification logic fails here
/// exactly as it would against Azure.
/// </para>
/// <para>
/// Objects are capped hard and held in memory. That is deliberate — a deterministic fixture is
/// a few kilobytes of synthetic video, and anything approaching a real master belongs in the
/// real adapter.
/// </para>
/// </remarks>
public sealed class DeterministicMediaStore
{
    /// <summary>Largest object the deterministic store will accept.</summary>
    public const int MaxObjectBytes = 8 * 1024 * 1024;

    private readonly ConcurrentDictionary<string, DeterministicObject> _objects = new(StringComparer.Ordinal);

    /// <summary>Accepts the bytes of one write.</summary>
    /// <returns>False when the payload is larger than the deterministic store will hold.</returns>
    public bool Write(string containerName, string blobName, byte[] content, string contentType)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Length is 0 or > MaxObjectBytes)
        {
            return false;
        }

        string etag = Convert.ToHexString(SHA256.HashData(content))[..32].ToLowerInvariant();

        _objects[Key(containerName, blobName)] = new DeterministicObject(
            content,
            contentType,
            $"\"{etag}\"",
            etag[..16]);

        return true;
    }

    /// <summary>Reads one object, or null when nothing was written there.</summary>
    public DeterministicObject? Read(string containerName, string blobName) =>
        _objects.GetValueOrDefault(Key(containerName, blobName));

    private static string Key(string containerName, string blobName) =>
        string.Create(CultureInfo.InvariantCulture, $"{containerName}/{blobName}");
}

/// <summary>
/// The storage adapter backed by <see cref="DeterministicMediaStore"/>.
/// </summary>
/// <remarks>
/// Upload authorisations point back at this application's own deterministic sink endpoint
/// rather than at a cloud host, so the browser performs a real cross-request upload against a
/// real URL and the client code under test is the same code that will talk to Azure.
/// </remarks>
public sealed class DeterministicMediaStorage(
    DeterministicMediaStore store,
    IOptions<MediaStorageOptions> options,
    TimeProvider timeProvider) : IMediaStorage
{
    /// <summary>Route the deterministic sink is mounted at.</summary>
    public const string SinkPath = "/api/media/deterministic-upload";

    private readonly MediaStorageOptions _options = options.Value;

    /// <inheritdoc />
    public ProviderMode Mode => ProviderMode.Deterministic;

    /// <inheritdoc />
    public Task<MediaUploadAuthorization> AuthorizeUploadAsync(
        string containerName,
        string blobName,
        string contentType,
        long declaredSizeBytes,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset expiresAt = timeProvider.GetUtcNow()
            .AddMinutes(_options.UploadWindowMinutes);

        // Relative on purpose: the sink is this API, so the client resolves it against the base
        // address it already uses and no host name has to be configured anywhere.
        Uri uploadUri = new(
            $"{SinkPath}/{Uri.EscapeDataString(containerName)}/{EscapeBlobName(blobName)}",
            UriKind.Relative);

        return Task.FromResult(new MediaUploadAuthorization(
            uploadUri,
            containerName,
            blobName,
            expiresAt,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = contentType,
            }));
    }

    /// <inheritdoc />
    public Task<MediaObjectProperties?> GetPropertiesAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        DeterministicObject? stored = store.Read(containerName, blobName);

        return Task.FromResult(stored is null
            ? null
            : new MediaObjectProperties(
                stored.ETag,
                stored.Content.Length,
                stored.ContentType,
                ContentMd5Base64: null,
                stored.VersionId));
    }

    /// <inheritdoc />
    public Task<MediaRestoreProbe?> ProbeRestoreAsync(
        string containerName,
        string blobName,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        DeterministicObject? stored = store.Read(containerName, blobName);

        if (stored is null)
        {
            return Task.FromResult<MediaRestoreProbe?>(null);
        }

        int take = maxBytes <= 0
            ? stored.Content.Length
            : (int)Math.Min(maxBytes, stored.Content.Length);

        ReadOnlySpan<byte> read = stored.Content.AsSpan(0, take);

        return Task.FromResult<MediaRestoreProbe?>(new MediaRestoreProbe(
            take,
            stored.Content.Length,
            Convert.ToHexString(SHA256.HashData(read)).ToLowerInvariant(),
            take == stored.Content.Length));
    }

    /// <inheritdoc />
    public Task<Uri> AuthorizeIngestReadAsync(
        string containerName,
        string blobName,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new Uri(
            $"{SinkPath}/{Uri.EscapeDataString(containerName)}/{EscapeBlobName(blobName)}",
            UriKind.Relative));

    /// <summary>Escapes each path segment while leaving the separators intact.</summary>
    private static string EscapeBlobName(string blobName) =>
        string.Join('/', blobName.Split('/').Select(Uri.EscapeDataString));
}
