namespace DanielsDojo.Domain.Media;

/// <summary>
/// A verified exact-source object in blob storage.
/// </summary>
/// <remarks>
/// <para>
/// This is the record of the master, not of the processed stream. The processing provider
/// produces an adaptive rendition for delivery; it is explicitly not a backup of the original,
/// so the properties captured here — exact version, length, and integrity hashes — are what a
/// restore is checked against before anyone considers deleting a local copy.
/// </para>
/// <para>
/// Rows are never deleted and never rewritten in place. A replacement is inserted alongside the
/// incumbent and only supersedes it once verified, which is what keeps a last known good source
/// available through a failed re-upload.
/// </para>
/// </remarks>
public sealed class MediaSource
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>The upload attempt that produced this object.</summary>
    public Guid UploadSessionId { get; set; }

    /// <summary>What the object is for.</summary>
    public MediaPurpose Purpose { get; set; }

    /// <summary>Owning course.</summary>
    public Guid CourseId { get; set; }

    /// <summary>Owning lesson, for lesson-scoped purposes.</summary>
    public Guid? LessonId { get; set; }

    /// <summary>Storage container holding the object.</summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>Blob name within the container.</summary>
    public string BlobName { get; set; } = string.Empty;

    /// <summary>
    /// Immutable blob version identifier. A restore is always performed against this exact
    /// version, so a later overwrite cannot silently change what was verified.
    /// </summary>
    public string? BlobVersionId { get; set; }

    /// <summary>Entity tag observed at finalisation.</summary>
    public string ETag { get; set; } = string.Empty;

    /// <summary>Length in bytes, read back from the store rather than trusted from the client.</summary>
    public long ContentLength { get; set; }

    /// <summary>Content type recorded on the stored object.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Base64 Content-MD5 as the storage service computed it. This is the provider's own
    /// transport integrity check, not our end-to-end one.
    /// </summary>
    public string? ContentMd5Base64 { get; set; }

    /// <summary>
    /// Lowercase hex SHA-256 of the source, computed by the uploader and re-computed on
    /// restore. This is the value a human deletion decision rests on.
    /// </summary>
    public string? ChecksumSha256 { get; set; }

    /// <summary>Whether this object is the one currently in use.</summary>
    public MediaSourceState State { get; set; } = MediaSourceState.Pending;

    /// <summary>Which provider wiring stored this object.</summary>
    public ProviderMode ProviderMode { get; set; }

    /// <summary>When blob properties were verified against the declaration, stored UTC.</summary>
    public DateTimeOffset? PropertiesVerifiedAtUtc { get; set; }

    /// <summary>When a streamed restore last matched the recorded checksum, stored UTC.</summary>
    public DateTimeOffset? RestoreVerifiedAtUtc { get; set; }

    /// <summary>Length observed during the last restore verification.</summary>
    public long? RestoreVerifiedLength { get; set; }

    /// <summary>When this object stopped being current, stored UTC.</summary>
    public DateTimeOffset? SupersededAtUtc { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The upload attempt that produced this object.</summary>
    public MediaUploadSession? UploadSession { get; set; }

    /// <summary>
    /// Whether the cloud copy has been proven to match the original well enough that a person
    /// could reasonably delete their local file. Playback evidence is tracked separately.
    /// </summary>
    public bool IsCloudVerified =>
        PropertiesVerifiedAtUtc is not null
        && RestoreVerifiedAtUtc is not null
        && RestoreVerifiedLength == ContentLength;
}
