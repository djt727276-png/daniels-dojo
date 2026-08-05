using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Domain.Media;
using DanielsDojo.Infrastructure.Media;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Catalog;
using DanielsDojo.IntegrationTests.Commerce;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DanielsDojo.IntegrationTests.Media;

/// <summary>
/// The media pipeline end to end over real HTTP, with deterministic providers.
/// </summary>
/// <remarks>
/// Nothing here is stubbed at the seam that matters. The browser upload is a genuine PUT to a
/// separate endpoint, the completion step really reads the object back, the notification is
/// really signed and really verified, and playback really mints and returns a token. The only
/// substitution is the provider itself.
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class MediaPipelineTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly byte[] Fixture = Encoding.UTF8.GetBytes(
        "synthetic master video fixture — tiny on purpose, never a real course video");

    /// <summary>A distinct second master, so a replacement is genuinely different bytes.</summary>
    private static readonly byte[] Replacement = Encoding.UTF8.GetBytes(
        "synthetic replacement master fixture — a different take of the same lesson");

    private ApiHarness _harness = null!;
    private TestActor _admin = null!;
    private TestActor _student = null!;
    private Guid _courseId;
    private Guid _lessonId;
    private Guid _articleLessonId;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        _harness = ApiHarness.Create(fixture);
        _admin = await _harness.SignInAsync(admin: true);
        _student = await _harness.SignInAsync();

        await SeedCourseAsync();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ------------------------------------------------------------------ happy path

    [Fact]
    public async Task AMasterTravelsFromBrowserToStorageToProviderToStudent()
    {
        using HttpClient admin = _harness.CreateClient(_admin);

        Guid sessionId = await RequestUploadAsync(admin);

        // The API never sees the bytes. They go to the storage endpoint directly, which is the
        // whole reason a multi-gigabyte master never lands on this process's disk.
        await UploadAsync(admin, sessionId, Fixture);

        using JsonDocument completed = await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/media/upload-sessions/{sessionId}/complete",
            null,
            HttpStatusCode.OK);

        JsonElement view = completed.RootElement;

        Assert.Equal("MuxIngesting", view.GetProperty("status").GetString());
        Assert.Equal("Deterministic", view.GetProperty("providerMode").GetString());
        Assert.False(view.GetProperty("isPlayable").GetBoolean());

        // Storage evidence exists before the provider has finished, because the master is
        // already safe at that point even though the lesson is not yet playable.
        JsonElement incoming = view.GetProperty("incomingSource");
        Assert.Equal(Fixture.Length, incoming.GetProperty("contentLength").GetInt64());
        Assert.False(incoming.GetProperty("checksumSha256").ValueKind == JsonValueKind.Null);

        await NotifyReadyAsync();

        using JsonDocument ready = await admin.GetJsonAsync($"/api/v1/admin/lessons/{_lessonId}/video");

        Assert.Equal("Ready", ready.RootElement.GetProperty("status").GetString());
        Assert.True(ready.RootElement.GetProperty("isPlayable").GetBoolean());
        Assert.Equal("Current", ready.RootElement.GetProperty("currentSource").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, ready.RootElement.GetProperty("incomingSource").ValueKind);

        // The student path issues a real signed token for a real playback identifier.
        using HttpClient student = _harness.CreateClient(_student);
        using JsonDocument playback = await student.GetJsonAsync(
            $"/api/v1/learning/lessons/{_lessonId}/playback");

        Assert.Equal("Membership", playback.RootElement.GetProperty("accessReason").GetString());
        Assert.False(string.IsNullOrWhiteSpace(playback.RootElement.GetProperty("token").GetString()));
        Assert.Equal(3, playback.RootElement.GetProperty("token").GetString()!.Split('.').Length);
    }

    [Fact]
    public async Task TheOriginalIsOnlySafeToDeleteAfterEveryCheckHasPassed()
    {
        using HttpClient admin = _harness.CreateClient(_admin);

        await CompleteReadyVideoAsync(admin);

        Assert.False(await SafeToDeleteAsync(admin));

        // A full read-back, hashed on the way past and discarded.
        using JsonDocument verified = await admin.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/admin/lessons/{_lessonId}/video/verify", null, HttpStatusCode.OK);

        JsonElement evidence = verified.RootElement.GetProperty("verification");
        Assert.True(evidence.GetProperty("restoreVerified").GetBoolean());
        Assert.False(evidence.GetProperty("safeToDeleteLocalOriginal").GetBoolean());

        await admin.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/admin/lessons/{_lessonId}/video/preview", null, HttpStatusCode.OK);

        Assert.False(await SafeToDeleteAsync(admin));

        using HttpClient student = _harness.CreateClient(_student);
        await student.GetJsonAsync($"/api/v1/learning/lessons/{_lessonId}/playback");

        // Everything automated has now passed, and it is still not safe: a machine cannot tell
        // whether the footage is the footage.
        Assert.False(await SafeToDeleteAsync(admin));

        await admin.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/admin/lessons/{_lessonId}/video/spot-check", null, HttpStatusCode.OK);

        Assert.True(await SafeToDeleteAsync(admin));
    }

    // ------------------------------------------------------------------ upload integrity

    [Fact]
    public async Task ClaimingAnUploadThatNeverHappenedIsRefused()
    {
        using HttpClient admin = _harness.CreateClient(_admin);

        Guid sessionId = await RequestUploadAsync(admin);

        // No PUT. The client simply asserts it finished.
        using JsonDocument problem = await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/media/upload-sessions/{sessionId}/complete",
            null,
            HttpStatusCode.Conflict);

        Assert.Equal("media.upload_missing", problem.ProblemCode());
    }

    [Fact]
    public async Task AnUploadThatDoesNotMatchWhatWasAuthorisedIsRefused()
    {
        using HttpClient admin = _harness.CreateClient(_admin);

        Guid sessionId = await RequestUploadAsync(admin, sizeBytes: Fixture.Length + 500);
        await UploadAsync(admin, sessionId, Fixture);

        using JsonDocument problem = await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/media/upload-sessions/{sessionId}/complete",
            null,
            HttpStatusCode.Conflict);

        Assert.Equal("media.upload_mismatch", problem.ProblemCode());
    }

    [Fact]
    public async Task AnUploadAuthorisationCannotBeUsedTwice()
    {
        using HttpClient admin = _harness.CreateClient(_admin);

        Guid sessionId = await RequestUploadAsync(admin);
        await UploadAsync(admin, sessionId, Fixture);

        await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/media/upload-sessions/{sessionId}/complete",
            null,
            HttpStatusCode.OK);

        using JsonDocument replayed = await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/media/upload-sessions/{sessionId}/complete",
            null,
            HttpStatusCode.Conflict);

        Assert.Equal("media.session_closed", replayed.ProblemCode());
    }

    [Fact]
    public async Task TheStorageSinkRefusesAWriteNobodyAuthorised()
    {
        using HttpClient admin = _harness.CreateClient(_admin);

        using ByteArrayContent content = new(Fixture);
        content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");

        using HttpResponseMessage response = await admin.PutAsync(
            new Uri(
                $"{DeterministicMediaStorage.SinkPath}/media-source/courses/anything/master.mp4",
                UriKind.Relative),
            content);

        // A storage endpoint that accepted arbitrary paths would be an open file host.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnArticleLessonCannotCarryAMasterVideo()
    {
        using HttpClient admin = _harness.CreateClient(_admin);

        using JsonDocument problem = await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/lessons/{_articleLessonId}/video/upload",
            new { fileName = "master.mp4", contentType = "video/mp4", sizeBytes = Fixture.Length },
            HttpStatusCode.BadRequest);

        Assert.Equal("media.not_a_video_lesson", problem.ProblemCode());
    }

    [Fact]
    public async Task AnUnacceptedContentTypeIsRefusedBeforeAnythingIsAuthorised()
    {
        using HttpClient admin = _harness.CreateClient(_admin);

        using JsonDocument problem = await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/lessons/{_lessonId}/video/upload",
            new { fileName = "master.exe", contentType = "application/x-msdownload", sizeBytes = 1024 },
            HttpStatusCode.BadRequest);

        Assert.Equal("media.unsupported_content_type", problem.ProblemCode());

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Empty(await context.MediaUploadSessions.ToListAsync());
    }

    // ------------------------------------------------------------------ notifications

    [Fact]
    public async Task AnUnsignedNotificationChangesNothing()
    {
        using HttpClient admin = _harness.CreateClient(_admin);
        await CompleteUploadAsync(admin);

        (string payload, _) = await ReadyNotificationAsync();

        using HttpClient anonymous = _harness.Factory.CreateClient();
        using StringContent content = new(payload, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await anonymous.PostAsync(
            new Uri("/api/v1/media/webhooks/video", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using JsonDocument view = await admin.GetJsonAsync($"/api/v1/admin/lessons/{_lessonId}/video");
        Assert.Equal("MuxIngesting", view.RootElement.GetProperty("status").GetString());

        // Nothing was recorded either — an anonymous caller cannot fill the audit trail.
        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.Empty(await context.WebhookEvents.ToListAsync());
    }

    [Fact]
    public async Task ARedeliveredNotificationIsAcceptedAndAppliedOnlyOnce()
    {
        using HttpClient admin = _harness.CreateClient(_admin);
        await CompleteUploadAsync(admin);

        await NotifyReadyAsync();
        await NotifyReadyAsync();

        await using DanielsDojoDbContext context = fixture.CreateContext();

        // One stored event, so a provider retry storm cannot multiply into repeated work.
        Assert.Single(await context.WebhookEvents.ToListAsync());
    }

    [Fact]
    public async Task AStaleNotificationCannotTakeAWorkingLessonOffTheAir()
    {
        using HttpClient admin = _harness.CreateClient(_admin);
        await CompleteReadyVideoAsync(admin);

        await PostNotificationAsync(await StaleProcessingPayloadAsync());

        using JsonDocument view = await admin.GetJsonAsync($"/api/v1/admin/lessons/{_lessonId}/video");

        Assert.Equal("Ready", view.RootElement.GetProperty("status").GetString());
        Assert.True(view.RootElement.GetProperty("isPlayable").GetBoolean());
    }

    [Fact]
    public async Task ReconciliationFinishesALessonWhoseNotificationNeverArrived()
    {
        using HttpClient admin = _harness.CreateClient(_admin);
        await CompleteUploadAsync(admin);

        // No notification at all — the delivery was lost.
        using JsonDocument report = await admin.SendJsonAsync(
            HttpMethod.Post, "/api/v1/admin/media/reconcile", null, HttpStatusCode.OK);

        Assert.Equal(1, report.RootElement.GetProperty("repaired").GetInt32());

        using JsonDocument view = await admin.GetJsonAsync($"/api/v1/admin/lessons/{_lessonId}/video");
        Assert.Equal("Ready", view.RootElement.GetProperty("status").GetString());
    }

    // ------------------------------------------------------------------ replacement

    [Fact]
    public async Task AReplacementKeepsTheOldVideoPlayingUntilTheNewOneIsReady()
    {
        using HttpClient admin = _harness.CreateClient(_admin);
        await CompleteReadyVideoAsync(admin);

        string originalPlaybackId = await PlaybackIdAsync(admin);

        Guid replacementSession = await RequestUploadAsync(admin, sizeBytes: Replacement.Length);

        using JsonDocument replacing = await admin.GetJsonAsync(
            $"/api/v1/admin/lessons/{_lessonId}/video");

        Assert.Equal("Replacing", replacing.RootElement.GetProperty("status").GetString());

        // The student is still watching throughout, on the previous asset.
        using HttpClient student = _harness.CreateClient(_student);
        using JsonDocument duringReplacement = await student.GetJsonAsync(
            $"/api/v1/learning/lessons/{_lessonId}/playback");

        Assert.Equal(
            originalPlaybackId,
            duringReplacement.RootElement.GetProperty("playbackId").GetString());

        await UploadAsync(admin, replacementSession, Replacement);
        await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/media/upload-sessions/{replacementSession}/complete",
            null,
            HttpStatusCode.OK);

        // Still serving the old one while the replacement processes.
        Assert.Equal(originalPlaybackId, await PlaybackIdAsync(admin));

        await using DanielsDojoDbContext context = fixture.CreateContext();

        // Two masters on record. The first is not deleted, moved, or overwritten.
        Assert.Equal(2, await context.MediaSources.CountAsync(source => source.LessonId == _lessonId));
        Assert.Equal(
            1,
            await context.MediaSources.CountAsync(source =>
                source.LessonId == _lessonId && source.State == MediaSourceState.Current));
    }

    [Fact]
    public async Task AFailedReplacementLeavesTheLessonExactlyWhereItStarted()
    {
        using HttpClient admin = _harness.CreateClient(_admin);
        await CompleteReadyVideoAsync(admin);

        string originalPlaybackId = await PlaybackIdAsync(admin);

        Guid replacementSession = await RequestUploadAsync(admin, sizeBytes: Replacement.Length);
        await UploadAsync(admin, replacementSession, Replacement);
        await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/media/upload-sessions/{replacementSession}/complete",
            null,
            HttpStatusCode.OK);

        await PostNotificationAsync(await ErroredNotificationAsync());

        using JsonDocument view = await admin.GetJsonAsync($"/api/v1/admin/lessons/{_lessonId}/video");

        Assert.Equal("Ready", view.RootElement.GetProperty("status").GetString());
        Assert.True(view.RootElement.GetProperty("isPlayable").GetBoolean());
        Assert.Equal(originalPlaybackId, await PlaybackIdAsync(admin));
    }

    // ------------------------------------------------------------------ authorization

    [Fact]
    public async Task AStudentCannotReachAnyAuthoringRoute()
    {
        using HttpClient student = _harness.CreateClient(_student);

        foreach (string path in new[]
        {
            $"/api/v1/admin/lessons/{_lessonId}/video/upload",
            $"/api/v1/admin/lessons/{_lessonId}/video/verify",
            $"/api/v1/admin/lessons/{_lessonId}/video/spot-check",
            "/api/v1/admin/media/reconcile",
        })
        {
            using HttpResponseMessage response = await student.SendJsonAsync(
                HttpMethod.Post,
                path,
                new { fileName = "x.mp4", contentType = "video/mp4", sizeBytes = 10 });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task AnAnonymousViewerIsRefusedPlaybackOfAPaidLesson()
    {
        using HttpClient admin = _harness.CreateClient(_admin);
        await CompleteReadyVideoAsync(admin);

        using HttpClient anonymous = _harness.Factory.CreateClient();
        using HttpResponseMessage response = await anonymous.GetAsync(
            new Uri($"/api/v1/learning/lessons/{_lessonId}/playback", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlaybackOfALessonWithNoVideoIsReportedAsNotReadyRatherThanBroken()
    {
        using HttpClient student = _harness.CreateClient(_student);

        using JsonDocument problem = await student.SendJsonAsync(
            HttpMethod.Get,
            $"/api/v1/learning/lessons/{_lessonId}/playback",
            null,
            HttpStatusCode.Conflict);

        Assert.Equal("media.not_ready", problem.ProblemCode());
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Guid> RequestUploadAsync(HttpClient admin, long? sizeBytes = null)
    {
        using JsonDocument ticket = await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/lessons/{_lessonId}/video/upload",
            new
            {
                fileName = "master.mp4",
                contentType = "video/mp4",
                sizeBytes = sizeBytes ?? Fixture.Length,
            },
            HttpStatusCode.OK);

        Assert.Equal("PUT", ticket.RootElement.GetProperty("httpMethod").GetString());
        Assert.Equal("Deterministic", ticket.RootElement.GetProperty("providerMode").GetString());

        return ticket.RootElement.GetProperty("sessionId").GetGuid();
    }

    private async Task UploadAsync(HttpClient admin, Guid sessionId, byte[] payload)
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        MediaUploadSession session = await context.MediaUploadSessions.SingleAsync(
            candidate => candidate.Id == sessionId);

        using ByteArrayContent content = new(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue(session.DeclaredContentType);

        using HttpResponseMessage response = await admin.PutAsync(
            new Uri(
                $"{DeterministicMediaStorage.SinkPath}/{session.ContainerName}/{session.BlobName}",
                UriKind.Relative),
            content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task CompleteUploadAsync(HttpClient admin)
    {
        Guid sessionId = await RequestUploadAsync(admin);
        await UploadAsync(admin, sessionId, Fixture);

        await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/media/upload-sessions/{sessionId}/complete",
            null,
            HttpStatusCode.OK);
    }

    private async Task CompleteReadyVideoAsync(HttpClient admin)
    {
        await CompleteUploadAsync(admin);
        await NotifyReadyAsync();
    }

    private async Task<bool> SafeToDeleteAsync(HttpClient admin)
    {
        using JsonDocument view = await admin.GetJsonAsync($"/api/v1/admin/lessons/{_lessonId}/video");

        return view.RootElement
            .GetProperty("verification")
            .GetProperty("safeToDeleteLocalOriginal")
            .GetBoolean();
    }

    private async Task<string> PlaybackIdAsync(HttpClient admin)
    {
        using JsonDocument preview = await admin.SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/admin/lessons/{_lessonId}/video/preview",
            null,
            HttpStatusCode.OK);

        return preview.RootElement.GetProperty("playbackId").GetString()!;
    }

    /// <summary>
    /// Builds and sends the notification the provider would send, signed by the same code the
    /// endpoint verifies with.
    /// </summary>
    private async Task NotifyReadyAsync() => await PostNotificationAsync(await ReadyNotificationAsync());

    private async Task<(string Payload, string Signature)> ReadyNotificationAsync()
    {
        (string assetId, string correlationKey) = await ProviderKeysAsync();

        DeterministicVideoPipeline pipeline = _harness.Factory.Services
            .GetRequiredService<DeterministicVideoPipeline>();

        return pipeline.CreateReadyNotification(assetId, correlationKey);
    }

    private async Task<(string Payload, string Signature)> ErroredNotificationAsync()
    {
        (string assetId, string correlationKey) = await ProviderKeysAsync();

        string payload = JsonSerializer.Serialize(new
        {
            id = $"event-errored-{Guid.NewGuid():N}",
            type = "video.asset.errored",
            created_at = DateTimeOffset.UtcNow.ToString("O"),
            data = new
            {
                id = assetId,
                status = "errored",
                passthrough = correlationKey,
                errors = new { type = "invalid_input" },
            },
        });

        return (payload, Signature(payload));
    }

    private async Task<(string Payload, string Signature)> StaleProcessingPayloadAsync()
    {
        (string assetId, string correlationKey) = await ProviderKeysAsync();

        string payload = JsonSerializer.Serialize(new
        {
            id = $"event-stale-{Guid.NewGuid():N}",
            type = "video.asset.created",

            // Names the right asset, but is dated before the ready event already applied to it.
            created_at = DateTimeOffset.UtcNow.AddHours(-2).ToString("O"),
            data = new { id = assetId, status = "preparing", passthrough = correlationKey },
        });

        return (payload, Signature(payload));
    }

    private static string Signature(string payload)
    {
        // Mirrors the provider's own scheme: the timestamp is inside the signed material, so a
        // captured delivery cannot be replayed with a fresh one.
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        byte[] signature = System.Security.Cryptography.HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(DeterministicVideoPipeline.DeterministicWebhookSecret),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));

        return $"t={timestamp},v1={Convert.ToHexString(signature).ToLowerInvariant()}";
    }

    private async Task PostNotificationAsync((string Payload, string Signature) notification)
    {
        using HttpClient anonymous = _harness.Factory.CreateClient();

        using HttpRequestMessage request = new(
            HttpMethod.Post,
            new Uri("/api/v1/media/webhooks/video", UriKind.Relative))
        {
            Content = new StringContent(notification.Payload, Encoding.UTF8, "application/json"),
        };

        request.Headers.TryAddWithoutValidation("Mux-Signature", notification.Signature);

        using HttpResponseMessage response = await anonymous.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task<(string AssetId, string CorrelationKey)> ProviderKeysAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        LessonVideo record = await context.LessonVideos.SingleAsync(
            candidate => candidate.LessonId == _lessonId);

        return (record.MuxAssetId!, record.MuxUploadId!);
    }

    private async Task SeedCourseAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        Course course = CatalogFactory.Course("media-course", "Media course", PublicationStatus.Published, true, now);
        CourseSection section = CatalogFactory.Section(course.Id, "Section", 0, PublicationStatus.Published, now);

        Lesson videoLesson = CatalogFactory.Lesson(
            course.Id, section.Id, "video-lesson", 0,
            PublicationStatus.Published, LessonType.Video, false, null, now);

        Lesson articleLesson = CatalogFactory.Lesson(
            course.Id, section.Id, "article-lesson", 1,
            PublicationStatus.Published, LessonType.Article, false, "Body.", now);

        context.Courses.Add(course);
        context.CourseSections.Add(section);
        context.Lessons.AddRange(videoLesson, articleLesson);

        // The student reaches this course through a real membership, backed by a real
        // subscription row. The host runs as Production here, so there is no Development
        // convenience grant to lean on — access has to come from the same place a paying
        // customer's would.
        OfferPrice membership = CommerceFactory.MembershipOffer(
            context, $"membership-{Guid.NewGuid():N}", now);

        Guid subscriptionId = CommerceFactory.Subscription(
            context,
            _student.UserId,
            membership,
            now.AddDays(-1),
            now.AddMonths(1),
            SubscriptionStatus.Active);

        context.Entitlements.Add(new Entitlement
        {
            Id = Guid.CreateVersion7(),
            UserId = _student.UserId,
            Scope = EntitlementScope.AllMembershipCourses,
            Source = EntitlementSource.Subscription,
            SubscriptionId = subscriptionId,
            Status = EntitlementStatus.Active,
            StartsAtUtc = now.AddDays(-1),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        await context.SaveChangesAsync();

        _courseId = course.Id;
        _lessonId = videoLesson.Id;
        _articleLessonId = articleLesson.Id;
    }
}
