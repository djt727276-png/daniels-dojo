using DanielsDojo.Api.Authentication;
using DanielsDojo.Api.Common;
using DanielsDojo.Application.Identity;
using DanielsDojo.Application.Media;
using DanielsDojo.Domain.Media;
using DanielsDojo.Infrastructure.Media;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DanielsDojo.Api.Media;

/// <summary>What an Admin declares before uploading.</summary>
/// <param name="FileName">Original file name, for display.</param>
/// <param name="ContentType">Declared content type.</param>
/// <param name="SizeBytes">Declared size in bytes.</param>
public sealed record UploadTicketRequest(string FileName, string ContentType, long SizeBytes);

/// <summary>Which caption language an upload is for.</summary>
/// <param name="LanguageCode">BCP-47 language code.</param>
/// <param name="FileName">Original file name, for display.</param>
/// <param name="ContentType">Declared content type.</param>
/// <param name="SizeBytes">Declared size in bytes.</param>
public sealed record CaptionUploadRequest(
    string LanguageCode,
    string FileName,
    string ContentType,
    long SizeBytes);

/// <summary>
/// Media authoring for Admins, playback for viewers, and the provider notification endpoint.
/// </summary>
/// <remarks>
/// The upload itself is not here, and that is the point: the browser writes straight to storage
/// with a short-lived single-object authorisation, so a multi-gigabyte master never crosses this
/// process. What these routes do is authorise the write beforehand and verify what landed
/// afterwards.
/// </remarks>
internal static class MediaEndpoints
{
    /// <summary>Maps the Admin media routes.</summary>
    public static void MapAdminMediaEndpoints(this RouteGroupBuilder apiV1)
    {
        RouteGroupBuilder media = apiV1
            .MapGroup("/admin/lessons/{lessonId:guid}/video")
            .RequireAuthorization(AuthenticationRegistration.AdminPolicy);

        media.MapGet("/", async (
                Guid lessonId,
                IAdminMediaService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.GetLessonVideoAsync(lessonId, cancellationToken)))
            .WithName("GetLessonVideo");

        media.MapPost("/upload", async (
                Guid lessonId,
                UploadTicketRequest request,
                IAdminMediaService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.RequestLessonVideoUploadAsync(
                    lessonId,
                    new MediaUploadRequest(request.FileName, request.ContentType, request.SizeBytes),
                    cancellationToken)))
            .WithName("RequestLessonVideoUpload");

        media.MapPost("/captions/upload", async (
                Guid lessonId,
                CaptionUploadRequest request,
                IAdminMediaService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.RequestCaptionUploadAsync(
                    lessonId,
                    request.LanguageCode,
                    new MediaUploadRequest(request.FileName, request.ContentType, request.SizeBytes),
                    cancellationToken)))
            .WithName("RequestCaptionUpload");

        // Verification is deliberately separate from completion. Completion runs a cheap probe
        // so authoring stays responsive; this reads the whole object back and is what an
        // administrator runs before deleting their local original.
        media.MapPost("/verify", async (
                Guid lessonId,
                IAdminMediaService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.VerifyRestoreAsync(lessonId, cancellationToken)))
            .WithName("VerifyLessonVideoRestore");

        media.MapPost("/preview", async (
                Guid lessonId,
                IAdminMediaService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.PreviewAsync(lessonId, cancellationToken)))
            .WithName("PreviewLessonVideo");

        media.MapPost("/spot-check", async (
                Guid lessonId,
                IAdminMediaService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.RecordSpotCheckAsync(lessonId, cancellationToken)))
            .WithName("RecordLessonVideoSpotCheck");

        media.MapPost("/reconcile", async (
                Guid lessonId,
                IAdminMediaService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.ReconcileAsync(lessonId, cancellationToken)))
            .WithName("ReconcileLessonVideo");

        apiV1.MapPost("/admin/media/upload-sessions/{sessionId:guid}/complete", async (
                Guid sessionId,
                IAdminMediaService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.CompleteUploadAsync(sessionId, cancellationToken)))
            .RequireAuthorization(AuthenticationRegistration.AdminPolicy)
            .WithName("CompleteMediaUpload");

        apiV1.MapPost("/admin/media/reconcile", async (
                IAdminMediaService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.ReconcileAsync(null, cancellationToken)))
            .RequireAuthorization(AuthenticationRegistration.AdminPolicy)
            .WithName("ReconcileAllMedia");
    }

    /// <summary>Maps the viewer playback route.</summary>
    public static void MapLearningMediaEndpoints(this RouteGroupBuilder apiV1)
    {
        // Anonymous is allowed through so a published preview lesson plays without a sign-in.
        // The access evaluator, not the route, decides what an anonymous viewer may have.
        apiV1.MapGet("/learning/lessons/{lessonId:guid}/playback", async (
                Guid lessonId,
                ICurrentUser currentUser,
                ILessonPlaybackService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.GetPlaybackAsync(
                    currentUser.User?.UserId, lessonId, cancellationToken)))
            .AllowAnonymous()
            .WithName("GetLessonPlayback");
    }

    /// <summary>Maps the provider notification endpoint.</summary>
    /// <remarks>
    /// Anonymous by necessity — the provider has no credential of ours — and therefore
    /// authenticated by signature instead. An unsigned or stale delivery gets 401 and changes
    /// nothing, and the raw body is read once and never logged.
    /// </remarks>
    public static void MapMediaWebhookEndpoints(this RouteGroupBuilder apiV1)
    {
        apiV1.MapPost("/media/webhooks/video", async (
                HttpRequest request,
                IMediaWebhookService service,
                CancellationToken cancellationToken) =>
            {
                using StreamReader reader = new(request.Body);
                string payload = await reader.ReadToEndAsync(cancellationToken);

                string? signature = request.Headers["Mux-Signature"].FirstOrDefault();

                return await service.HandleVideoEventAsync(payload, signature, cancellationToken)
                    ? Results.Accepted()
                    : Results.Unauthorized();
            })
            .AllowAnonymous()
            .WithName("ReceiveVideoProviderEvent");
    }

    /// <summary>
    /// Mounts the deterministic upload sink.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the local stand-in for the cloud storage endpoint, so that the browser performs a
    /// genuine cross-request upload during development and in the deterministic suites. It is
    /// mapped only when storage is running in deterministic mode — in any other mode the route
    /// does not exist at all, which is a stronger guarantee than a runtime check.
    /// </para>
    /// <para>
    /// It accepts a write only where an upload session has authorised one, so it cannot be used
    /// as an open file host even in Development.
    /// </para>
    /// </remarks>
    public static void MapDeterministicMediaSink(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MediaStorageOptions options = app.Services
            .GetRequiredService<IOptions<MediaStorageOptions>>().Value;

        if (options.Mode != ProviderMode.Deterministic)
        {
            return;
        }

        app.MapPut($"{DeterministicMediaStorage.SinkPath}/{{containerName}}/{{**blobName}}", async (
                string containerName,
                string blobName,
                HttpRequest request,
                DeterministicMediaStore store,
                DanielsDojo.Infrastructure.Persistence.DanielsDojoDbContext context,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                DateTimeOffset now = timeProvider.GetUtcNow();

                bool authorized = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .AnyAsync(
                        context.MediaUploadSessions.Where(session =>
                            session.ContainerName == containerName
                            && session.BlobName == blobName
                            && (session.Status == MediaUploadStatus.Requested
                                || session.Status == MediaUploadStatus.Uploading)
                            && session.ExpiresAtUtc > now),
                        cancellationToken);

                if (!authorized)
                {
                    return Results.NotFound();
                }

                using MemoryStream buffer = new();
                await request.Body.CopyToAsync(buffer, cancellationToken);

                return store.Write(
                    containerName,
                    blobName,
                    buffer.ToArray(),
                    request.ContentType ?? "application/octet-stream")
                    ? Results.Created()
                    : Results.BadRequest(new ProblemDetails
                    {
                        Title = "Upload rejected",
                        Detail =
                            "The deterministic store holds fixtures only, up to "
                            + $"{DeterministicMediaStore.MaxObjectBytes} bytes. Point the "
                            + "application at real storage to upload a genuine master.",
                        Status = StatusCodes.Status400BadRequest,
                    });
            })
            .AllowAnonymous()
            .WithName("DeterministicMediaUpload");
    }
}
