namespace DanielsDojo.Application.Media;

/// <summary>
/// An authorisation for a browser to write exactly one object.
/// </summary>
/// <param name="UploadUri">
/// Where the bytes go. This is handed to the browser, is write-only, is scoped to this single
/// object, and expires. It is never logged and never persisted.
/// </param>
/// <param name="ContainerName">The container the object will live in.</param>
/// <param name="BlobName">The server-chosen object name. A client never picks this.</param>
/// <param name="ExpiresAtUtc">When the authorisation stops working.</param>
/// <param name="RequiredHeaders">
/// Headers the browser must send for the write to be accepted, so the stored object carries the
/// content type the server decided on rather than one the client asserted later.
/// </param>
public sealed record MediaUploadAuthorization(
    Uri UploadUri,
    string ContainerName,
    string BlobName,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyDictionary<string, string> RequiredHeaders);

/// <summary>What the storage service reports about a stored object.</summary>
/// <param name="ETag">The service's version marker for the object as stored.</param>
/// <param name="ContentLength">Bytes the service is holding.</param>
/// <param name="ContentType">Content type recorded against the object.</param>
/// <param name="ContentMd5Base64">Transport integrity hash, when the service computed one.</param>
/// <param name="VersionId">Immutable version identifier, when versioning is enabled.</param>
public sealed record MediaObjectProperties(
    string ETag,
    long ContentLength,
    string ContentType,
    string? ContentMd5Base64,
    string? VersionId);

/// <summary>The result of reading an object back out of storage.</summary>
/// <param name="BytesRead">How many bytes actually came back.</param>
/// <param name="ReportedLength">The length the service reports for the whole object.</param>
/// <param name="Sha256">Hash of the bytes that were read.</param>
/// <param name="IsComplete">
/// Whether the whole object was read. A partial read proves the object is retrievable; only a
/// complete read proves the bytes are the bytes, which is the difference between "probably
/// fine" and "safe to delete the only other copy".
/// </param>
public sealed record MediaRestoreProbe(
    long BytesRead,
    long ReportedLength,
    string Sha256,
    bool IsComplete);

/// <summary>
/// Exact-source object storage.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no delete. The application must never be able to remove an original,
/// because for a period the copy in storage is the only copy that has been verified and the
/// local one is about to be deleted by hand. A retention decision is a human one made against
/// the storage account, not something a code path can reach.
/// </para>
/// <para>
/// Uploads never pass through the API. The browser writes straight to storage with a
/// short-lived single-object authorisation, so a multi-gigabyte master never lands on the API's
/// disk and never creates a second local copy.
/// </para>
/// </remarks>
public interface IMediaStorage
{
    /// <summary>Which adapter is serving, so a stored row can record what produced it.</summary>
    Domain.Media.ProviderMode Mode { get; }

    /// <summary>Authorises exactly one write to one server-named object.</summary>
    Task<MediaUploadAuthorization> AuthorizeUploadAsync(
        string containerName,
        string blobName,
        string contentType,
        long declaredSizeBytes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads what the service holds for an object, or null when nothing is there. Null is the
    /// answer when a client claimed an upload finished that never actually happened.
    /// </summary>
    Task<MediaObjectProperties?> GetPropertiesAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads bytes back out to prove the object is genuinely retrievable, not merely listed.
    /// </summary>
    /// <param name="maxBytes">
    /// Read at most this many bytes, or zero to read the entire object. The bytes are hashed as
    /// they stream past and are never written anywhere, so verifying a multi-gigabyte master
    /// costs bandwidth and nothing else — it never produces the second local copy the whole
    /// design exists to avoid.
    /// </param>
    Task<MediaRestoreProbe?> ProbeRestoreAsync(
        string containerName,
        string blobName,
        long maxBytes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a short-lived read authorisation for the video provider to pull the master once.
    /// </summary>
    Task<Uri> AuthorizeIngestReadAsync(
        string containerName,
        string blobName,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);
}
