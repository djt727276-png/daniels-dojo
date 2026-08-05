using System.Globalization;
using DanielsDojo.Application.Common;
using DanielsDojo.Application.Identity;
using DanielsDojo.Application.Media;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Media;
using DanielsDojo.Infrastructure.Auditing;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DanielsDojo.Infrastructure.Media;

/// <summary>
/// The Admin media pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The governing rule is that nothing is believed until it has been checked against the storage
/// service itself. A client reporting a finished upload is a prompt to go and look, never
/// evidence; a provider notification is applied only if it is signed, current, and newer than
/// what has already been applied.
/// </para>
/// <para>
/// A replacement never disturbs the video that is already working. The incoming master is
/// tracked separately, the previous asset keeps serving throughout, and the swap happens in one
/// step once the new asset is genuinely playable. If the replacement fails, the lesson is
/// exactly where it started.
/// </para>
/// </remarks>
internal sealed class AdminMediaService : IAdminMediaService
{
    private readonly DanielsDojoDbContext context;
    private readonly IMediaStorage storage;
    private readonly IVideoPipeline video;
    private readonly ICurrentUser currentUser;
    private readonly TimeProvider timeProvider;
    private readonly MediaStorageOptions storageOptions;
    private readonly VideoProviderOptions videoOptions;
    private readonly VideoStateMachine stateMachine;
    private readonly AuditTrail audit;

    public AdminMediaService(
        DanielsDojoDbContext context,
        IMediaStorage storage,
        IVideoPipeline video,
        ICurrentUser currentUser,
        IOperationContext operationContext,
        IOptions<MediaStorageOptions> storageOptions,
        IOptions<VideoProviderOptions> videoOptions,
        TimeProvider timeProvider)
    {
        this.context = context;
        this.storage = storage;
        this.video = video;
        this.currentUser = currentUser;
        this.timeProvider = timeProvider;
        this.storageOptions = storageOptions.Value;
        this.videoOptions = videoOptions.Value;

        stateMachine = new VideoStateMachine(context);
        audit = new AuditTrail(context, operationContext, timeProvider);
    }

    // ------------------------------------------------------------------ upload

    public async Task<OperationResult<MediaUploadTicket>> RequestLessonVideoUploadAsync(
        Guid lessonId,
        MediaUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (storage.Mode == ProviderMode.Disabled)
        {
            return Disabled<MediaUploadTicket>();
        }

        Lesson? lesson = await context.Lessons
            .FirstOrDefaultAsync(candidate => candidate.Id == lessonId, cancellationToken);

        if (lesson is null)
        {
            return OperationResult.NotFound().ToFailure<MediaUploadTicket>();
        }

        if (lesson.LessonType != LessonType.Video)
        {
            return OperationResult.Invalid(
                MediaErrorCodes.NotAVideoLesson,
                "lessonId",
                "Only a video lesson can carry a master video.")
                .ToFailure<MediaUploadTicket>();
        }

        if (Validate(request, MediaProjections.VideoContentTypes) is { } invalid)
        {
            return invalid.ToFailure<MediaUploadTicket>();
        }

        LessonVideo record = await LoadOrCreateVideoAsync(lesson, cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();

        bool isReplacement = record.Status is LessonVideoStatus.Ready or LessonVideoStatus.Replacing;

        MediaUploadSession session = NewSession(
            MediaPurpose.LessonVideo,
            lesson.CourseId,
            lesson.Id,
            request,
            isReplacement,
            now);

        MediaUploadAuthorization authorization = await storage.AuthorizeUploadAsync(
            session.ContainerName,
            session.BlobName,
            session.DeclaredContentType,
            session.DeclaredSizeBytes,
            cancellationToken);

        session.ExpiresAtUtc = authorization.ExpiresAtUtc;
        context.MediaUploadSessions.Add(session);

        if (isReplacement)
        {
            // The working asset is pinned before anything about the replacement is written, so
            // there is no window in which a student loses playback because a new upload started.
            record.LastKnownGoodAssetId ??= record.MuxAssetId;
            record.LastKnownGoodPlaybackId ??= record.MuxPlaybackId;

            if (record.Status == LessonVideoStatus.Ready)
            {
                record.Status = LessonVideoStatus.Replacing;
            }
        }
        else
        {
            record.Status = LessonVideoStatus.Requested;
            record.FailureCode = null;
        }

        record.UpdatedAtUtc = now;

        audit.Append(
            "Media.Upload.Requested",
            nameof(Lesson),
            lesson.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sessionId"] = session.Id.ToString("D"),
                ["purpose"] = session.Purpose.ToString(),
                ["providerMode"] = session.ProviderMode.ToString(),
                ["isReplacement"] = isReplacement ? "true" : "false",
                ["declaredSizeBytes"] = session.DeclaredSizeBytes.ToString(CultureInfo.InvariantCulture),
            });

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.FromValue(new MediaUploadTicket(
            session.Id,
            authorization.UploadUri,
            "PUT",
            authorization.RequiredHeaders,
            authorization.ExpiresAtUtc,
            storage.Mode.ToString()));
    }

    public async Task<OperationResult<MediaUploadTicket>> RequestCaptionUploadAsync(
        Guid lessonId,
        string languageCode,
        MediaUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (storage.Mode == ProviderMode.Disabled)
        {
            return Disabled<MediaUploadTicket>();
        }

        if (string.IsNullOrWhiteSpace(languageCode) || languageCode.Length > 16)
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "languageCode",
                "Provide a language code of up to 16 characters.")
                .ToFailure<MediaUploadTicket>();
        }

        Lesson? lesson = await context.Lessons
            .FirstOrDefaultAsync(candidate => candidate.Id == lessonId, cancellationToken);

        if (lesson is null)
        {
            return OperationResult.NotFound().ToFailure<MediaUploadTicket>();
        }

        if (Validate(request, MediaProjections.CaptionContentTypes) is { } invalid)
        {
            return invalid.ToFailure<MediaUploadTicket>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        MediaUploadSession session = NewSession(
            MediaPurpose.CaptionTrack,
            lesson.CourseId,
            lesson.Id,
            request,
            isReplacement: false,
            now);

        MediaUploadAuthorization authorization = await storage.AuthorizeUploadAsync(
            session.ContainerName,
            session.BlobName,
            session.DeclaredContentType,
            session.DeclaredSizeBytes,
            cancellationToken);

        session.ExpiresAtUtc = authorization.ExpiresAtUtc;
        session.OriginalFileName = languageCode;

        context.MediaUploadSessions.Add(session);

        audit.Append(
            "Media.Upload.Requested",
            nameof(Lesson),
            lesson.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sessionId"] = session.Id.ToString("D"),
                ["purpose"] = session.Purpose.ToString(),
                ["languageCode"] = languageCode,
            });

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.FromValue(new MediaUploadTicket(
            session.Id,
            authorization.UploadUri,
            "PUT",
            authorization.RequiredHeaders,
            authorization.ExpiresAtUtc,
            storage.Mode.ToString()));
    }

    public async Task<OperationResult<LessonVideoView>> CompleteUploadAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        MediaUploadSession? session = await context.MediaUploadSessions
            .FirstOrDefaultAsync(candidate => candidate.Id == sessionId, cancellationToken);

        if (session is null)
        {
            return OperationResult.NotFound().ToFailure<LessonVideoView>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        if (!session.IsOpenAt(now))
        {
            return OperationResult.Conflict(
                MediaErrorCodes.SessionClosed,
                "This upload authorisation has expired or was already completed. Start a new upload.")
                .ToFailure<LessonVideoView>();
        }

        // The client claims the bytes landed. Go and ask the service that actually holds them.
        MediaObjectProperties? properties = await storage.GetPropertiesAsync(
            session.ContainerName,
            session.BlobName,
            cancellationToken);

        if (properties is null)
        {
            return await FailSessionAsync(
                session,
                MediaErrorCodes.UploadMissing,
                "No object was found at the authorised location. The upload did not complete.",
                now,
                cancellationToken);
        }

        if (properties.ContentLength != session.DeclaredSizeBytes)
        {
            return await FailSessionAsync(
                session,
                MediaErrorCodes.UploadMismatch,
                "The stored object does not match the size that was authorised.",
                now,
                cancellationToken);
        }

        MediaRestoreProbe? probe = await storage.ProbeRestoreAsync(
            session.ContainerName,
            session.BlobName,
            storageOptions.RestoreProbeBytes,
            cancellationToken);

        if (probe is null || probe.ReportedLength != properties.ContentLength)
        {
            return await FailSessionAsync(
                session,
                MediaErrorCodes.RestoreFailed,
                "The stored object could not be read back.",
                now,
                cancellationToken);
        }

        var source = new MediaSource
        {
            Id = Guid.CreateVersion7(),
            UploadSessionId = session.Id,
            Purpose = session.Purpose,
            CourseId = session.CourseId,
            LessonId = session.LessonId,
            ContainerName = session.ContainerName,
            BlobName = session.BlobName,
            BlobVersionId = properties.VersionId,
            ETag = properties.ETag,
            ContentLength = properties.ContentLength,
            ContentType = properties.ContentType,
            ContentMd5Base64 = properties.ContentMd5Base64,
            ChecksumSha256 = probe.IsComplete ? probe.Sha256 : null,
            State = MediaSourceState.Pending,
            ProviderMode = storage.Mode,
            PropertiesVerifiedAtUtc = now,
            RestoreVerifiedAtUtc = probe.IsComplete ? now : null,
            RestoreVerifiedLength = probe.IsComplete ? probe.BytesRead : null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.MediaSources.Add(source);

        session.Status = MediaUploadStatus.Completed;
        session.CompletedAtUtc = now;
        session.UpdatedAtUtc = now;

        audit.Append(
            "Media.Source.Stored",
            nameof(MediaSource),
            source.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sessionId"] = session.Id.ToString("D"),
                ["contentLength"] = source.ContentLength.ToString(CultureInfo.InvariantCulture),
                ["restoreComplete"] = probe.IsComplete ? "true" : "false",
                ["providerMode"] = source.ProviderMode.ToString(),
            });

        if (session.Purpose == MediaPurpose.CaptionTrack)
        {
            await AttachCaptionAsync(session, source, now, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            return await BuildViewAsync(session.LessonId!.Value, cancellationToken);
        }

        await StartIngestAsync(session, source, now, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return await BuildViewAsync(session.LessonId!.Value, cancellationToken);
    }

    // ------------------------------------------------------------------ verification

    public async Task<OperationResult<LessonVideoView>> GetLessonVideoAsync(
        Guid lessonId,
        CancellationToken cancellationToken = default)
    {
        bool exists = await context.Lessons
            .AnyAsync(lesson => lesson.Id == lessonId, cancellationToken);

        return exists
            ? await BuildViewAsync(lessonId, cancellationToken)
            : OperationResult.NotFound().ToFailure<LessonVideoView>();
    }

    public async Task<OperationResult<LessonVideoView>> VerifyRestoreAsync(
        Guid lessonId,
        CancellationToken cancellationToken = default)
    {
        LessonVideo? record = await context.LessonVideos
            .FirstOrDefaultAsync(candidate => candidate.LessonId == lessonId, cancellationToken);

        Guid? sourceId = record?.CurrentSourceId ?? record?.IncomingSourceId;

        if (record is null || sourceId is null)
        {
            return OperationResult.NotFound().ToFailure<LessonVideoView>();
        }

        MediaSource? source = await context.MediaSources
            .FirstOrDefaultAsync(candidate => candidate.Id == sourceId, cancellationToken);

        if (source is null)
        {
            return OperationResult.NotFound().ToFailure<LessonVideoView>();
        }

        // maxBytes zero means read all of it. This is the expensive, conclusive check: the whole
        // object streams past a hash and is discarded, so it proves the bytes without ever
        // producing a second copy of them.
        MediaRestoreProbe? probe = await storage.ProbeRestoreAsync(
            source.ContainerName,
            source.BlobName,
            maxBytes: 0,
            cancellationToken);

        DateTimeOffset now = timeProvider.GetUtcNow();

        if (probe is null || !probe.IsComplete || probe.BytesRead != source.ContentLength)
        {
            audit.Append(
                "Media.Source.RestoreFailed",
                nameof(MediaSource),
                source.Id,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["expectedLength"] = source.ContentLength.ToString(CultureInfo.InvariantCulture),
                    ["bytesRead"] = (probe?.BytesRead ?? 0).ToString(CultureInfo.InvariantCulture),
                });

            await context.SaveChangesAsync(cancellationToken);

            return OperationResult.Conflict(
                MediaErrorCodes.RestoreFailed,
                "The stored object did not read back completely. Do not delete the local original.")
                .ToFailure<LessonVideoView>();
        }

        if (source.ChecksumSha256 is { Length: > 0 } previous
            && !string.Equals(previous, probe.Sha256, StringComparison.Ordinal))
        {
            // The object changed underneath a verification that already passed. That is a
            // reason to stop, not to overwrite the earlier record with the new hash.
            audit.Append(
                "Media.Source.ChecksumChanged",
                nameof(MediaSource),
                source.Id);

            await context.SaveChangesAsync(cancellationToken);

            return OperationResult.Conflict(
                MediaErrorCodes.RestoreFailed,
                "The stored object no longer matches its recorded checksum.")
                .ToFailure<LessonVideoView>();
        }

        source.ChecksumSha256 = probe.Sha256;
        source.RestoreVerifiedAtUtc = now;
        source.RestoreVerifiedLength = probe.BytesRead;
        source.PropertiesVerifiedAtUtc ??= now;
        source.UpdatedAtUtc = now;

        audit.Append(
            "Media.Source.RestoreVerified",
            nameof(MediaSource),
            source.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bytesRead"] = probe.BytesRead.ToString(CultureInfo.InvariantCulture),
                ["checksumSha256"] = probe.Sha256,
            });

        await context.SaveChangesAsync(cancellationToken);

        return await BuildViewAsync(lessonId, cancellationToken);
    }

    public async Task<OperationResult<LessonPlaybackGrant>> PreviewAsync(
        Guid lessonId,
        CancellationToken cancellationToken = default)
    {
        LessonVideo? record = await context.LessonVideos
            .Include(candidate => candidate.CaptionTracks)
            .FirstOrDefaultAsync(candidate => candidate.LessonId == lessonId, cancellationToken);

        if (record is null)
        {
            return OperationResult.NotFound().ToFailure<LessonPlaybackGrant>();
        }

        if (record.ServablePlaybackId is not { Length: > 0 } playbackId)
        {
            return OperationResult.Conflict(
                MediaErrorCodes.NotReady,
                "This lesson has no playable video yet.")
                .ToFailure<LessonPlaybackGrant>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset expiresAt = now.AddMinutes(videoOptions.PlaybackTokenMinutes);

        record.AdminPlaybackVerifiedAtUtc = now;
        record.UpdatedAtUtc = now;

        audit.Append("Media.Playback.AdminVerified", nameof(LessonVideo), record.Id);

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.FromValue(new LessonPlaybackGrant(
            lessonId,
            playbackId,
            video.UsesSignedPlayback ? video.CreatePlaybackToken(playbackId, expiresAt) : null,
            expiresAt,
            record.DurationSeconds,
            record.AspectRatio,
            [.. record.CaptionTracks.Select(MediaProjections.ToView)],
            "AdminPreview"));
    }

    public async Task<OperationResult<LessonVideoView>> RecordSpotCheckAsync(
        Guid lessonId,
        CancellationToken cancellationToken = default)
    {
        LessonVideo? record = await context.LessonVideos
            .FirstOrDefaultAsync(candidate => candidate.LessonId == lessonId, cancellationToken);

        if (record is null)
        {
            return OperationResult.NotFound().ToFailure<LessonVideoView>();
        }

        if (currentUser.User is not { } actor)
        {
            return OperationResult.Forbidden(
                MediaErrorCodes.ProviderDisabled,
                "A spot check must be attributed to a signed-in administrator.")
                .ToFailure<LessonVideoView>();
        }

        if (record.Status != LessonVideoStatus.Ready)
        {
            return OperationResult.Conflict(
                MediaErrorCodes.NotReady,
                "Only a playable video can be signed off.")
                .ToFailure<LessonVideoView>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        record.HumanSpotCheckAtUtc = now;
        record.HumanSpotCheckByUserId = actor.UserId;
        record.UpdatedAtUtc = now;

        audit.Append(
            "Media.Verification.SpotChecked",
            nameof(LessonVideo),
            record.Id,
            reason: "A person confirmed the processed video is the intended footage.");

        await context.SaveChangesAsync(cancellationToken);

        return await BuildViewAsync(lessonId, cancellationToken);
    }

    // ------------------------------------------------------------------ reconciliation

    public async Task<OperationResult<MediaReconciliationReport>> ReconcileAsync(
        Guid? lessonId,
        CancellationToken cancellationToken = default)
    {
        if (video.Mode == ProviderMode.Disabled)
        {
            return Disabled<MediaReconciliationReport>();
        }

        IQueryable<LessonVideo> query = context.LessonVideos
            .Where(record => record.MuxAssetId != null
                && record.Status != LessonVideoStatus.Archived);

        if (lessonId is { } target)
        {
            query = query.Where(record => record.LessonId == target);
        }
        else
        {
            // A lesson that is already playable and not mid-replacement has nothing to repair.
            query = query.Where(record => record.Status != LessonVideoStatus.Ready);
        }

        List<LessonVideo> candidates = await query
            .OrderBy(record => record.UpdatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        int repaired = 0;
        int pending = 0;
        int unreachable = 0;

        DateTimeOffset now = timeProvider.GetUtcNow();

        foreach (LessonVideo record in candidates)
        {
            VideoAssetState? state = await video.GetAssetAsync(record.MuxAssetId!, cancellationToken);

            if (state is null)
            {
                unreachable++;

                audit.Append(
                    "Media.Reconciliation.AssetMissing",
                    nameof(LessonVideo),
                    record.Id);

                continue;
            }

            if (stateMachine.Apply(record, state, now))
            {
                repaired++;
            }
            else if (!MediaLifecycle.IsPlayable(record.Status))
            {
                pending++;
            }
        }

        if (repaired > 0 || unreachable > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return OperationResult.FromValue(new MediaReconciliationReport(
            candidates.Count,
            repaired,
            pending,
            unreachable));
    }

    // ------------------------------------------------------------------ helpers

    private async Task StartIngestAsync(
        MediaUploadSession session,
        MediaSource source,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        LessonVideo? record = await context.LessonVideos
            .FirstOrDefaultAsync(candidate => candidate.LessonId == session.LessonId, cancellationToken);

        if (record is null)
        {
            return;
        }

        record.IncomingSourceId = source.Id;
        record.ProviderMode = video.Mode;
        record.IsSignedPlayback = video.UsesSignedPlayback;
        record.UpdatedAtUtc = now;

        if (video.Mode == ProviderMode.Disabled)
        {
            // Storage worked and processing is switched off. The master is recorded and safe;
            // the lesson simply is not playable, which is the truthful state to report.
            if (record.Status != LessonVideoStatus.Replacing)
            {
                record.Status = LessonVideoStatus.AzureStored;
            }

            return;
        }

        Uri readUri = await storage.AuthorizeIngestReadAsync(
            source.ContainerName,
            source.BlobName,
            TimeSpan.FromMinutes(storageOptions.IngestReadWindowMinutes),
            cancellationToken);

        VideoIngestTicket ticket = await video.StartIngestAsync(
            readUri,
            record.Id.ToString("D"),
            cancellationToken);

        record.MuxUploadId = ticket.UploadId;

        if (ticket.AssetId is { Length: > 0 })
        {
            record.MuxAssetId = ticket.AssetId;
        }

        if (record.Status != LessonVideoStatus.Replacing)
        {
            record.Status = LessonVideoStatus.MuxIngesting;
        }

        audit.Append(
            "Media.Ingest.Started",
            nameof(LessonVideo),
            record.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sourceId"] = source.Id.ToString("D"),
                ["providerMode"] = video.Mode.ToString(),
                ["isReplacement"] = session.IsReplacement ? "true" : "false",
            });
    }

    private async Task AttachCaptionAsync(
        MediaUploadSession session,
        MediaSource source,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        LessonVideo? record = await context.LessonVideos
            .Include(candidate => candidate.CaptionTracks)
            .FirstOrDefaultAsync(candidate => candidate.LessonId == session.LessonId, cancellationToken);

        if (record is null)
        {
            return;
        }

        string languageCode = session.OriginalFileName ?? "en";

        MediaCaptionTrack? existing = record.CaptionTracks
            .FirstOrDefault(track => string.Equals(
                track.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase));

        var track = existing ?? new MediaCaptionTrack
        {
            Id = Guid.CreateVersion7(),
            LessonVideoId = record.Id,
            LanguageCode = languageCode,
            DisplayName = languageCode.ToUpperInvariant(),
            IsDefault = record.CaptionTracks.Count == 0,
            CreatedAtUtc = now,
        };

        track.MediaSourceId = source.Id;
        track.Status = LessonVideoStatus.Requested;
        track.UpdatedAtUtc = now;

        if (existing is null)
        {
            context.MediaCaptionTracks.Add(track);
        }

        source.State = MediaSourceState.Current;
        source.UpdatedAtUtc = now;

        if (video.Mode != ProviderMode.Disabled && record.MuxAssetId is { Length: > 0 } assetId)
        {
            Uri readUri = await storage.AuthorizeIngestReadAsync(
                source.ContainerName,
                source.BlobName,
                TimeSpan.FromMinutes(storageOptions.IngestReadWindowMinutes),
                cancellationToken);

            VideoCaptionTicket ticket = await video.AddCaptionTrackAsync(
                assetId,
                readUri,
                languageCode,
                track.DisplayName,
                cancellationToken);

            track.ProviderTrackId = ticket.TrackId;
            track.Status = ticket.Status;
        }

        audit.Append(
            "Media.Caption.Attached",
            nameof(LessonVideo),
            record.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["languageCode"] = languageCode,
                ["sourceId"] = source.Id.ToString("D"),
            });
    }

    private async Task<LessonVideo> LoadOrCreateVideoAsync(Lesson lesson, CancellationToken cancellationToken)
    {
        LessonVideo? existing = await context.LessonVideos
            .FirstOrDefaultAsync(candidate => candidate.LessonId == lesson.Id, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        var created = new LessonVideo
        {
            Id = Guid.CreateVersion7(),
            LessonId = lesson.Id,
            Status = LessonVideoStatus.Requested,
            ProviderMode = video.Mode,
            IsSignedPlayback = video.UsesSignedPlayback,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.LessonVideos.Add(created);

        return created;
    }

    private MediaUploadSession NewSession(
        MediaPurpose purpose,
        Guid courseId,
        Guid lessonId,
        MediaUploadRequest request,
        bool isReplacement,
        DateTimeOffset now)
    {
        Guid sessionId = Guid.CreateVersion7();

        return new MediaUploadSession
        {
            Id = sessionId,
            Purpose = purpose,
            CourseId = courseId,
            LessonId = lessonId,
            RequestedByUserId = currentUser.User?.UserId ?? Guid.Empty,
            ContainerName = storageOptions.SourceContainer,
            BlobName = MediaProjections.BuildBlobName(
                purpose, courseId, lessonId, sessionId, request.ContentType),
            OriginalFileName = Truncate(request.FileName, 256),
            DeclaredSizeBytes = request.SizeBytes,
            DeclaredContentType = request.ContentType,
            IsReplacement = isReplacement,
            Status = MediaUploadStatus.Requested,
            ProviderMode = storage.Mode,
            ExpiresAtUtc = now.AddMinutes(storageOptions.UploadWindowMinutes),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private OperationResult? Validate(
        MediaUploadRequest request,
        IReadOnlyDictionary<string, string> acceptedContentTypes)
    {
        if (string.IsNullOrWhiteSpace(request.ContentType)
            || !acceptedContentTypes.ContainsKey(request.ContentType))
        {
            return OperationResult.Invalid(
                MediaErrorCodes.UnsupportedContentType,
                "contentType",
                $"Accepted types are {string.Join(", ", acceptedContentTypes.Keys)}.");
        }

        if (request.SizeBytes <= 0)
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "sizeBytes",
                "Provide the size of the file you are uploading.");
        }

        if (request.SizeBytes > storageOptions.MaxUploadBytes)
        {
            return OperationResult.Invalid(
                MediaErrorCodes.UploadTooLarge,
                "sizeBytes",
                $"The largest accepted upload is {storageOptions.MaxUploadBytes} bytes.");
        }

        return null;
    }

    private async Task<OperationResult<LessonVideoView>> FailSessionAsync(
        MediaUploadSession session,
        string code,
        string message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        session.Status = MediaUploadStatus.Failed;
        session.FailureCode = code;
        session.UpdatedAtUtc = now;

        audit.Append(
            "Media.Upload.Failed",
            nameof(MediaUploadSession),
            session.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["failureCode"] = code,
            });

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Conflict(code, message).ToFailure<LessonVideoView>();
    }

    private async Task<OperationResult<LessonVideoView>> BuildViewAsync(
        Guid lessonId,
        CancellationToken cancellationToken)
    {
        LessonVideo? record = await context.LessonVideos
            .AsNoTracking()
            .Include(candidate => candidate.CaptionTracks)
            .FirstOrDefaultAsync(candidate => candidate.LessonId == lessonId, cancellationToken);

        if (record is null)
        {
            return OperationResult.FromValue(
                MediaProjections.ToView(lessonId, null, null, null, []));
        }

        MediaSource? current = record.CurrentSourceId is { } currentId
            ? await context.MediaSources.AsNoTracking()
                .FirstOrDefaultAsync(source => source.Id == currentId, cancellationToken)
            : null;

        MediaSource? incoming = record.IncomingSourceId is { } incomingId
            ? await context.MediaSources.AsNoTracking()
                .FirstOrDefaultAsync(source => source.Id == incomingId, cancellationToken)
            : null;

        return OperationResult.FromValue(MediaProjections.ToView(
            lessonId,
            record,
            current,
            incoming,
            [.. record.CaptionTracks]));
    }

    private static OperationResult<T> Disabled<T>() =>
        OperationResult.Conflict(
            MediaErrorCodes.ProviderDisabled,
            "Media providers are switched off in this environment.")
            .ToFailure<T>();

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];
}
