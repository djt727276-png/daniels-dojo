using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Community;

/// <summary>
/// A member's avatar image.
/// </summary>
/// <remarks>
/// <para>
/// The stored bytes are never the file the member uploaded. Every upload is decoded as a
/// raster image and re-encoded server-side into a small, fixed-size JPEG, so nothing the
/// client controlled — metadata, embedded payloads, an SVG's scripts — survives into
/// storage or reaches another member's browser.
/// </para>
/// <para>
/// Kept in the database rather than object storage: at a few kilobytes per member the
/// bytes are trivially transactional, they disappear with the account when it is deleted,
/// and no public container or signed URL ever exists for them.
/// </para>
/// </remarks>
public sealed class ProfileAvatar
{
    /// <summary>Owning platform user. Primary key and foreign key.</summary>
    public Guid UserId { get; set; }

    /// <summary>Content type of the stored bytes. Always a server-chosen raster type.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>The re-encoded image bytes.</summary>
    public byte[] Bytes { get; set; } = [];

    /// <summary>Hash of the stored bytes, doubling as a cache validator.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last replacement instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>The owning platform user.</summary>
    public User? User { get; set; }
}
