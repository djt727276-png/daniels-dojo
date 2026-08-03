namespace DanielsDojo.Domain.Catalog;

/// <summary>
/// Video provider metadata for one lesson. Metadata only — no video bytes, signed
/// playback tokens, or provider secrets are stored.
/// </summary>
public sealed class LessonVideo
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning lesson. Unique: at most one video record per lesson.</summary>
    public Guid LessonId { get; set; }

    /// <summary>Provider asset identifier. Unique when present.</summary>
    public string? MuxAssetId { get; set; }

    /// <summary>Provider playback identifier. Unique when present.</summary>
    public string? MuxPlaybackId { get; set; }

    /// <summary>Provider processing state.</summary>
    public LessonVideoStatus Status { get; set; } = LessonVideoStatus.Pending;

    /// <summary>Measured duration reported by the provider.</summary>
    public int? DurationSeconds { get; set; }

    /// <summary>Short provider failure code when <see cref="Status"/> is Errored.</summary>
    public string? FailureCode { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token; provider events update this row.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The owning lesson.</summary>
    public Lesson? Lesson { get; set; }
}
