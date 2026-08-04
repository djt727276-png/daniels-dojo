using System.Buffers;
using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using DanielsDojo.Application.Media;
using DanielsDojo.Domain.Media;
using Microsoft.Extensions.Options;

namespace DanielsDojo.Infrastructure.Media;

/// <summary>
/// Exact-source storage backed by Azure Blob Storage.
/// </summary>
/// <remarks>
/// <para>
/// Uploads are authorised with a user delegation SAS: a short-lived, write-only permission
/// scoped to one blob and signed by an Entra identity. That is stronger than an account-key SAS
/// in the way that matters here — the delegation key can be revoked and expires on its own,
/// where a leaked account key grants everything until somebody notices and rotates it.
/// </para>
/// <para>
/// Nothing in this class deletes. There is no code path, not even an unused one, that could
/// remove a master, because during the window between "verified in Azure" and "deleted locally"
/// the blob is the only copy that has been checked.
/// </para>
/// </remarks>
public sealed class AzureBlobMediaStorage(
    BlobServiceClient serviceClient,
    IOptions<MediaStorageOptions> options,
    TimeProvider timeProvider) : IMediaStorage
{
    private readonly MediaStorageOptions _options = options.Value;

    /// <inheritdoc />
    public ProviderMode Mode => ProviderMode.Real;

    /// <inheritdoc />
    public async Task<MediaUploadAuthorization> AuthorizeUploadAsync(
        string containerName,
        string blobName,
        string contentType,
        long declaredSizeBytes,
        CancellationToken cancellationToken = default)
    {
        BlobClient blob = serviceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobName);

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset expiresAt = now.AddMinutes(_options.UploadWindowMinutes);

        // Create only, never write-over: a replayed authorisation cannot overwrite an existing
        // master even if it is still inside its validity window.
        var permissions = BlobSasPermissions.Create | BlobSasPermissions.Write;

        Uri uploadUri = await SignAsync(blob, permissions, now, expiresAt, contentType, cancellationToken);

        return new MediaUploadAuthorization(
            uploadUri,
            containerName,
            blobName,
            expiresAt,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-ms-blob-type"] = "BlockBlob",
                ["Content-Type"] = contentType,
            });
    }

    /// <inheritdoc />
    public async Task<MediaObjectProperties?> GetPropertiesAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        BlobClient blob = serviceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobName);

        try
        {
            Response<BlobProperties> response = await blob.GetPropertiesAsync(
                cancellationToken: cancellationToken);

            BlobProperties properties = response.Value;

            return new MediaObjectProperties(
                properties.ETag.ToString(),
                properties.ContentLength,
                properties.ContentType ?? "application/octet-stream",
                properties.ContentHash is { Length: > 0 }
                    ? Convert.ToBase64String(properties.ContentHash)
                    : null,
                properties.VersionId);
        }
        catch (RequestFailedException failure) when (failure.Status == 404)
        {
            // The client said the upload finished and the service disagrees. That is an answer,
            // not a fault.
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<MediaRestoreProbe?> ProbeRestoreAsync(
        string containerName,
        string blobName,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        BlobClient blob = serviceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobName);

        try
        {
            HttpRange range = maxBytes <= 0 ? default : new HttpRange(0, maxBytes);

            Response<BlobDownloadStreamingResult> response = await blob.DownloadStreamingAsync(
                new BlobDownloadOptions { Range = range },
                cancellationToken);

            using BlobDownloadStreamingResult download = response.Value;

            long reportedLength = download.Details.ContentLength;

            if (download.Details.ContentRange is { } contentRange)
            {
                reportedLength = ParseTotalLength(contentRange) ?? reportedLength;
            }

            (long bytesRead, string hash) = await HashStreamAsync(download.Content, cancellationToken);

            return new MediaRestoreProbe(bytesRead, reportedLength, hash, bytesRead == reportedLength);
        }
        catch (RequestFailedException failure) when (failure.Status is 404 or 416)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<Uri> AuthorizeIngestReadAsync(
        string containerName,
        string blobName,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        BlobClient blob = serviceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobName);

        DateTimeOffset now = timeProvider.GetUtcNow();

        return await SignAsync(
            blob,
            BlobSasPermissions.Read,
            now,
            now.Add(lifetime),
            contentType: null,
            cancellationToken);
    }

    /// <summary>
    /// Signs a SAS, preferring a user delegation key and falling back to the client's own
    /// credential when the account is configured with a shared key.
    /// </summary>
    private async Task<Uri> SignAsync(
        BlobClient blob,
        BlobSasPermissions permissions,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        string? contentType,
        CancellationToken cancellationToken)
    {
        BlobSasBuilder builder = new(permissions, expiresAt)
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            Resource = "b",

            // Backdated a little so a small clock difference between this host and Azure does
            // not reject an authorisation the moment it is issued.
            StartsOn = now.AddMinutes(-5),
            Protocol = SasProtocol.Https,
        };

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            builder.ContentType = contentType;
        }

        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(builder);
        }

        UserDelegationKey delegationKey = await serviceClient.GetUserDelegationKeyAsync(
            now.AddMinutes(-5),
            expiresAt,
            cancellationToken);

        BlobUriBuilder uriBuilder = new(blob.Uri)
        {
            Sas = builder.ToSasQueryParameters(delegationKey, serviceClient.AccountName),
        };

        return uriBuilder.ToUri();
    }

    /// <summary>
    /// Hashes a stream as it goes past, holding one pooled buffer and never a whole object.
    /// </summary>
    private static async Task<(long BytesRead, string Sha256)> HashStreamAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        long total = 0;

        try
        {
            int read;

            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hasher.AppendData(buffer, 0, read);
                total += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return (total, Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant());
    }

    /// <summary>Reads the total object size out of a <c>bytes 0-1023/5000</c> content range.</summary>
    private static long? ParseTotalLength(string contentRange)
    {
        int slash = contentRange.LastIndexOf('/');

        return slash >= 0
            && long.TryParse(
                contentRange.AsSpan(slash + 1),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out long total)
            ? total
            : null;
    }
}
