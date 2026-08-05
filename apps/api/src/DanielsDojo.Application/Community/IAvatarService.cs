using DanielsDojo.Application.Common;

namespace DanielsDojo.Application.Community;

/// <summary>An avatar as it is served: bytes plus the headers that describe them.</summary>
/// <param name="Bytes">The stored, server-encoded image bytes.</param>
/// <param name="ContentType">Content type of those bytes. Always a server-chosen raster type.</param>
/// <param name="ETag">Cache validator derived from the stored hash.</param>
public sealed record AvatarContent(byte[] Bytes, string ContentType, string ETag);

/// <summary>
/// Member avatars.
/// </summary>
/// <remarks>
/// The invariant this service owns: no byte a client produced is ever stored or served.
/// An upload must decode as a raster image — SVG and anything else non-raster fails the
/// decode — and what is stored is a fresh fixed-size JPEG encoded here, so metadata,
/// polyglot payloads, and active content are shed by construction rather than filtered.
/// </remarks>
public interface IAvatarService
{
    /// <summary>Error codes this service reports.</summary>
    public static class Errors
    {
        /// <summary>The bytes are not a decodable raster image.</summary>
        public const string NotAnImage = "avatars.not_an_image";

        /// <summary>The upload exceeds the accepted size.</summary>
        public const string TooLarge = "avatars.too_large";
    }

    /// <summary>Largest accepted upload, in bytes.</summary>
    public const long MaxUploadBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Replaces the member's avatar with a re-encoded copy of the uploaded image.
    /// </summary>
    Task<OperationResult> SetAsync(
        Guid userId,
        Stream content,
        long declaredLength,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the member's avatar. Removing a missing avatar succeeds.</summary>
    Task<OperationResult> RemoveAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a member's avatar for a given reader, or null when there is none — including
    /// when a block in either direction means the reader must not see this member at all.
    /// </summary>
    Task<AvatarContent?> GetAsync(
        Guid readerUserId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default);
}
