namespace DanielsDojo.Domain.Catalog;

/// <summary>
/// A downloadable file attached to a lesson. Stores the blob object name only; SAS URLs
/// are minted per request and never persisted.
/// </summary>
public sealed class LessonResource
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning lesson.</summary>
    public Guid LessonId { get; set; }

    /// <summary>Name shown to students.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Blob object name. Nullable while the resource is a draft; required once the
    /// resource is published, which is enforced by a check constraint.
    /// </summary>
    public string? BlobObjectName { get; set; }

    /// <summary>
    /// The verified source object holding this file. Null for rows created before a source
    /// was recorded; required once the resource is published.
    /// </summary>
    public Guid? MediaSourceId { get; set; }

    /// <summary>IANA media type.</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>Size in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Position within the lesson.</summary>
    public int SortOrder { get; set; }

    /// <summary>Publication state.</summary>
    public PublicationStatus Status { get; set; } = PublicationStatus.Draft;

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The owning lesson.</summary>
    public Lesson? Lesson { get; set; }

    /// <summary>The verified source object holding this file.</summary>
    public Media.MediaSource? MediaSource { get; set; }
}
