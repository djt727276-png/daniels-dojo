using System.Security.Cryptography;
using DanielsDojo.Application.Common;
using DanielsDojo.Application.Community;
using DanielsDojo.Domain.Community;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace DanielsDojo.Infrastructure.Community;

/// <summary>
/// Avatar processing and storage.
/// </summary>
/// <remarks>
/// The uploaded file is treated as untrusted bytes, never as an image of the type it
/// claims. It must decode as a raster image — which an SVG, an HTML polyglot, or random
/// bytes cannot — and the only thing stored is a fresh 256×256 JPEG this service encodes
/// itself. Client metadata (EXIF, GPS, colour profiles, appended payloads) does not
/// survive re-encoding, so it cannot leak to other members.
/// </remarks>
internal sealed class AvatarService : IAvatarService
{
    /// <summary>Both dimensions of the stored image.</summary>
    private const int TargetSize = 256;

    /// <summary>Decode ceiling: refuse absurd pixel dimensions before allocating for them.</summary>
    private const int MaxSourcePixelsPerSide = 8192;

    private readonly DanielsDojoDbContext context;
    private readonly ICommunityAccessEvaluator accessEvaluator;
    private readonly TimeProvider timeProvider;

    public AvatarService(
        DanielsDojoDbContext context,
        ICommunityAccessEvaluator accessEvaluator,
        TimeProvider timeProvider)
    {
        this.context = context;
        this.accessEvaluator = accessEvaluator;
        this.timeProvider = timeProvider;
    }

    public async Task<OperationResult> SetAsync(
        Guid userId,
        Stream content,
        long declaredLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        CommunityAccess access = await accessEvaluator.EvaluateAsync(userId, cancellationToken);

        if (!access.Granted)
        {
            return OperationResult.Forbidden(
                access.Denial == CommunityAccessDenial.SetupRequired
                    ? ErrorCodes.CommunitySetupRequired
                    : ErrorCodes.CommunityForbidden,
                access.Message ?? "You cannot take part in the community right now.");
        }

        if (declaredLength is <= 0 or > IAvatarService.MaxUploadBytes)
        {
            return OperationResult.Invalid(
                IAvatarService.Errors.TooLarge,
                "file",
                "Choose an image up to 2 MB.");
        }

        // Buffer with a hard cap one byte past the limit, so a lying Content-Length cannot
        // push an oversized body through.
        using MemoryStream buffer = new();
        await CopyBoundedAsync(content, buffer, IAvatarService.MaxUploadBytes, cancellationToken);

        if (buffer.Length is 0 or > IAvatarService.MaxUploadBytes)
        {
            return OperationResult.Invalid(
                IAvatarService.Errors.TooLarge,
                "file",
                "Choose an image up to 2 MB.");
        }

        buffer.Position = 0;
        byte[] encoded;

        try
        {
            using Image image = await Image.LoadAsync(buffer, cancellationToken);

            if (image.Width > MaxSourcePixelsPerSide || image.Height > MaxSourcePixelsPerSide)
            {
                return NotAnImage();
            }

            // Cover-crop to a square, then a fixed size: every stored avatar has identical
            // dimensions, and the encoder writes no metadata sections we did not create.
            image.Mutate(operation => operation.Resize(new ResizeOptions
            {
                Size = new Size(TargetSize, TargetSize),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center,
            }));

            using MemoryStream output = new();
            await image.SaveAsync(
                output, new JpegEncoder { Quality = 85 }, cancellationToken);
            encoded = output.ToArray();
        }
        catch (ImageFormatException)
        {
            // Covers unknown formats and corrupt content alike: not decodable, not stored.
            return NotAnImage();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        ProfileAvatar? existing = await context.ProfileAvatars
            .FirstOrDefaultAsync(avatar => avatar.UserId == userId, cancellationToken);

        if (existing is null)
        {
            existing = new ProfileAvatar
            {
                UserId = userId,
                CreatedAtUtc = now,
            };
            context.ProfileAvatars.Add(existing);
        }

        existing.ContentType = "image/jpeg";
        existing.Bytes = encoded;
        existing.Sha256 = Convert.ToHexStringLower(SHA256.HashData(encoded));
        existing.UpdatedAtUtc = now;

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    public async Task<OperationResult> RemoveAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ProfileAvatar? existing = await context.ProfileAvatars
            .FirstOrDefaultAsync(avatar => avatar.UserId == userId, cancellationToken);

        if (existing is not null)
        {
            context.ProfileAvatars.Remove(existing);
            await context.SaveChangesAsync(cancellationToken);
        }

        return OperationResult.Success();
    }

    public async Task<AvatarContent?> GetAsync(
        Guid readerUserId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        // A block in either direction hides the member entirely — the avatar included, and
        // indistinguishably from the member simply having none.
        if (readerUserId != ownerUserId
            && await context.UserBlocks.AnyAsync(
                block => (block.BlockerUserId == readerUserId && block.BlockedUserId == ownerUserId)
                    || (block.BlockerUserId == ownerUserId && block.BlockedUserId == readerUserId),
                cancellationToken))
        {
            return null;
        }

        var stored = await context.ProfileAvatars
            .AsNoTracking()
            .Where(avatar => avatar.UserId == ownerUserId)
            .Select(avatar => new { avatar.Bytes, avatar.ContentType, avatar.Sha256 })
            .FirstOrDefaultAsync(cancellationToken);

        return stored is null
            ? null
            : new AvatarContent(stored.Bytes, stored.ContentType, $"\"{stored.Sha256}\"");
    }

    private static OperationResult NotAnImage() =>
        OperationResult.Invalid(
            IAvatarService.Errors.NotAnImage,
            "file",
            "Choose a JPEG, PNG, GIF, or WebP image. SVG is not accepted.");

    /// <summary>Copies at most one byte past <paramref name="limit"/>, then stops.</summary>
    private static async Task CopyBoundedAsync(
        Stream source,
        MemoryStream destination,
        long limit,
        CancellationToken cancellationToken)
    {
        byte[] chunk = new byte[64 * 1024];
        long remaining = limit + 1;

        while (remaining > 0)
        {
            int read = await source.ReadAsync(
                chunk.AsMemory(0, (int)Math.Min(chunk.Length, remaining)), cancellationToken);

            if (read == 0)
            {
                return;
            }

            destination.Write(chunk, 0, read);
            remaining -= read;
        }
    }
}
