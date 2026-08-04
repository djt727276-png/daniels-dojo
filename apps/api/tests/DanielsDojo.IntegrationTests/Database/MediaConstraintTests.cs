using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Domain.Media;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Database;

/// <summary>
/// The database-level guarantees the media pipeline is built on.
/// </summary>
/// <remarks>
/// <para>
/// These are the rules that have to hold even when the application is wrong: a retried upload
/// that would overwrite somebody's master, a half-finished replacement that would leave a lesson
/// with two current sources, or a verification record claiming a restore happened without saying
/// how many bytes came back. Application code enforces all of them too, but a defect there is a
/// bug while a defect here is lost footage.
/// </para>
/// <para>
/// Everything asserted below is enforced by SQL, so it survives a future code path that forgets
/// to ask.
/// </para>
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class MediaConstraintTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    private Guid _courseId;
    private Guid _lessonId;
    private Guid _userId;

    public async Task InitializeAsync()
    {
        await fixture.ResetWithoutSeedAsync();

        await using DanielsDojoDbContext context = fixture.CreateContext();

        User user = TestEntities.User();
        Course course = TestEntities.Course();
        CourseSection section = TestEntities.Section(course.Id, 1);
        Lesson lesson = TestEntities.Lesson(course.Id, section.Id);

        context.Users.Add(user);
        context.Courses.Add(course);
        context.CourseSections.Add(section);
        context.Lessons.Add(lesson);

        await context.SaveChangesAsync();

        _userId = user.Id;
        _courseId = course.Id;
        _lessonId = lesson.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------- upload sessions

    [Fact]
    public async Task TwoSessionsCannotBeAuthorisedAgainstTheSameBlob()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        context.MediaUploadSessions.Add(Session("courses/one/lesson/master.mp4"));
        await context.SaveChangesAsync();

        // A retry that reused the name would let the second writer land on the first one's bytes.
        context.MediaUploadSessions.Add(Session("courses/one/lesson/master.mp4"));

        await AssertViolationAsync(context, "UX_UploadSessions_BlobName");
    }

    [Fact]
    public async Task ALessonScopedUploadMustNameItsLesson()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        MediaUploadSession orphan = Session("courses/one/orphan.mp4");
        orphan.LessonId = null;

        context.MediaUploadSessions.Add(orphan);

        await AssertViolationAsync(context, "CK_UploadSessions_LessonScope");
    }

    [Fact]
    public async Task ACourseScopedUploadMustNotNameALesson()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        MediaUploadSession image = Session("courses/one/cover.jpg");
        image.Purpose = MediaPurpose.CourseImage;
        image.DeclaredContentType = "image/jpeg";

        context.MediaUploadSessions.Add(image);

        await AssertViolationAsync(context, "CK_UploadSessions_LessonScope");
    }

    [Fact]
    public async Task ACompletedSessionMustRecordWhenAndAnOpenOneMustNot()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        MediaUploadSession completedWithoutTime = Session("courses/one/a.mp4");
        completedWithoutTime.Status = MediaUploadStatus.Completed;

        context.MediaUploadSessions.Add(completedWithoutTime);
        await AssertViolationAsync(context, "CK_UploadSessions_CompletedAt");

        await using DanielsDojoDbContext second = fixture.CreateContext();

        MediaUploadSession openWithTime = Session("courses/one/b.mp4");
        openWithTime.CompletedAtUtc = Now;

        second.MediaUploadSessions.Add(openWithTime);
        await AssertViolationAsync(second, "CK_UploadSessions_CompletedAt");
    }

    [Fact]
    public async Task AnEmptyUploadIsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        MediaUploadSession empty = Session("courses/one/empty.mp4");
        empty.DeclaredSizeBytes = 0;

        context.MediaUploadSessions.Add(empty);

        await AssertViolationAsync(context, "CK_UploadSessions_DeclaredSize_Positive");
    }

    // ------------------------------------------------------------- sources

    [Fact]
    public async Task ALessonCannotEndUpWithTwoCurrentVideoSources()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        await AddSourceAsync(context, "courses/one/master-v1.mp4", MediaSourceState.Current);

        // This is the half-finished replacement: the new master goes Current while the old one
        // was never superseded. The database is what stops the lesson serving two masters.
        MediaSource second = await StageSourceAsync(context, "courses/one/master-v2.mp4");
        second.State = MediaSourceState.Current;
        context.MediaSources.Add(second);

        await AssertViolationAsync(context, "UX_Sources_LessonId_Purpose_Current");
    }

    [Fact]
    public async Task ASupersededSourceStillCountsAndDoesNotBlockTheNewCurrentOne()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        MediaSource original = await AddSourceAsync(
            context, "courses/one/master-v1.mp4", MediaSourceState.Current);

        original.State = MediaSourceState.Superseded;
        original.SupersededAtUtc = Now;
        await context.SaveChangesAsync();

        MediaSource replacement = await StageSourceAsync(context, "courses/one/master-v2.mp4");
        replacement.State = MediaSourceState.Current;
        context.MediaSources.Add(replacement);

        await context.SaveChangesAsync();

        // The old master is still on record. Replacement supersedes; it never deletes.
        Assert.Equal(2, await context.MediaSources.CountAsync(source => source.LessonId == _lessonId));
    }

    [Fact]
    public async Task OneUploadSessionProducesAtMostOneStoredObject()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        MediaSource first = await AddSourceAsync(
            context, "courses/one/master.mp4", MediaSourceState.Pending);

        MediaSource duplicate = NewSource(first.UploadSessionId, "courses/one/master.mp4");
        context.MediaSources.Add(duplicate);

        await AssertViolationAsync(context, "UX_Sources_UploadSessionId");
    }

    [Fact]
    public async Task ARestoreClaimWithoutAMeasuredLengthIsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        MediaSource source = await StageSourceAsync(context, "courses/one/master.mp4");
        source.RestoreVerifiedAtUtc = Now;
        source.RestoreVerifiedLength = null;

        context.MediaSources.Add(source);

        // "We restored it" means nothing without the number of bytes that came back.
        await AssertViolationAsync(context, "CK_Sources_RestoreEvidenceComplete");
    }

    [Fact]
    public async Task ASupersededSourceMustRecordWhenItStoppedBeingCurrent()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        MediaSource source = await StageSourceAsync(context, "courses/one/master.mp4");
        source.State = MediaSourceState.Superseded;
        source.SupersededAtUtc = null;

        context.MediaSources.Add(source);

        await AssertViolationAsync(context, "CK_Sources_SupersededAt");
    }

    [Fact]
    public async Task AZeroLengthSourceIsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        MediaSource source = await StageSourceAsync(context, "courses/one/master.mp4");
        source.ContentLength = 0;

        context.MediaSources.Add(source);

        await AssertViolationAsync(context, "CK_Sources_ContentLength_Positive");
    }

    [Fact]
    public async Task AConcurrentEditToASourceIsDetected()
    {
        MediaSource seeded;

        await using (DanielsDojoDbContext setup = fixture.CreateContext())
        {
            seeded = await AddSourceAsync(setup, "courses/one/master.mp4", MediaSourceState.Pending);
        }

        await using DanielsDojoDbContext first = fixture.CreateContext();
        await using DanielsDojoDbContext second = fixture.CreateContext();

        MediaSource mine = await first.MediaSources.SingleAsync(source => source.Id == seeded.Id);
        MediaSource theirs = await second.MediaSources.SingleAsync(source => source.Id == seeded.Id);

        mine.PropertiesVerifiedAtUtc = Now;
        await first.SaveChangesAsync();

        // A verification racing a replacement must lose rather than overwrite it silently.
        theirs.State = MediaSourceState.Superseded;
        theirs.SupersededAtUtc = Now;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    // ------------------------------------------------------------- lesson video

    [Fact]
    public async Task AReadyVideoWithoutAPlaybackIdentifierIsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        LessonVideo video = TestEntities.LessonVideo(_lessonId, assetId: "asset-1");
        video.Status = LessonVideoStatus.Ready;
        video.MuxPlaybackId = null;

        context.LessonVideos.Add(video);

        // Ready means a student can press play, so there has to be something to play.
        await AssertViolationAsync(context, "CK_LessonVideos_ReadyRequiresPlayback");
    }

    [Fact]
    public async Task AReplacingVideoWithoutALastKnownGoodIsRejected()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        LessonVideo video = TestEntities.LessonVideo(_lessonId, "asset-1", "playback-1");
        video.Status = LessonVideoStatus.Replacing;
        video.LastKnownGoodPlaybackId = null;

        context.LessonVideos.Add(video);

        // Replacement is only safe because the previous asset keeps serving throughout.
        await AssertViolationAsync(context, "CK_LessonVideos_ReplacingRequiresLastKnownGood");
    }

    [Fact]
    public async Task AFailedVideoMustSayWhy()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        LessonVideo video = TestEntities.LessonVideo(_lessonId);
        video.Status = LessonVideoStatus.Failed;
        video.FailureCode = null;

        context.LessonVideos.Add(video);

        await AssertViolationAsync(context, "CK_LessonVideos_FailureCode");
    }

    [Fact]
    public async Task AHumanSpotCheckMustNameTheHuman()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        LessonVideo video = TestEntities.LessonVideo(_lessonId, "asset-1", "playback-1");
        video.HumanSpotCheckAtUtc = Now;
        video.HumanSpotCheckByUserId = null;

        context.LessonVideos.Add(video);

        // An unattributed sign-off is not evidence that anybody watched it.
        await AssertViolationAsync(context, "CK_LessonVideos_SpotCheckActor");
    }

    // ------------------------------------------------------------- caption tracks

    [Fact]
    public async Task AVideoCannotCarryTwoTracksForTheSameLanguage()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        LessonVideo video = TestEntities.LessonVideo(_lessonId, "asset-1", "playback-1");
        video.Status = LessonVideoStatus.Ready;
        context.LessonVideos.Add(video);

        MediaSource source = await AddSourceAsync(
            context, "courses/one/captions.vtt", MediaSourceState.Current, MediaPurpose.CaptionTrack);

        context.MediaCaptionTracks.Add(Track(video.Id, source.Id, "en"));
        await context.SaveChangesAsync();

        context.MediaCaptionTracks.Add(Track(video.Id, source.Id, "en"));

        await AssertViolationAsync(context, "UX_CaptionTracks_LessonVideoId_LanguageCode");
    }

    [Fact]
    public async Task AProviderTrackIdentifierIsNeverReused()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        LessonVideo video = TestEntities.LessonVideo(_lessonId, "asset-1", "playback-1");
        video.Status = LessonVideoStatus.Ready;
        context.LessonVideos.Add(video);

        MediaSource source = await AddSourceAsync(
            context, "courses/one/captions.vtt", MediaSourceState.Current, MediaPurpose.CaptionTrack);

        MediaCaptionTrack english = Track(video.Id, source.Id, "en");
        english.ProviderTrackId = "track-abc";
        context.MediaCaptionTracks.Add(english);
        await context.SaveChangesAsync();

        MediaCaptionTrack spanish = Track(video.Id, source.Id, "es");
        spanish.ProviderTrackId = "track-abc";
        context.MediaCaptionTracks.Add(spanish);

        await AssertViolationAsync(context, "UX_CaptionTracks_ProviderTrackId");
    }

    // ------------------------------------------------------------- helpers

    private MediaUploadSession Session(string blobName) => new()
    {
        Id = Guid.NewGuid(),
        Purpose = MediaPurpose.LessonVideo,
        CourseId = _courseId,
        LessonId = _lessonId,
        RequestedByUserId = _userId,
        ContainerName = "media-source",
        BlobName = blobName,
        OriginalFileName = "master.mp4",
        DeclaredSizeBytes = 4096,
        DeclaredContentType = "video/mp4",
        Status = MediaUploadStatus.Requested,
        ProviderMode = ProviderMode.Deterministic,
        ExpiresAtUtc = Now.AddHours(1),
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    private MediaSource NewSource(
        Guid uploadSessionId,
        string blobName,
        MediaPurpose purpose = MediaPurpose.LessonVideo) => new()
        {
            Id = Guid.NewGuid(),
            UploadSessionId = uploadSessionId,
            Purpose = purpose,
            CourseId = _courseId,
            LessonId = _lessonId,
            ContainerName = "media-source",
            BlobName = blobName,
            ETag = $"\"{Guid.NewGuid():N}\"",
            ContentLength = 4096,
            ContentType = purpose == MediaPurpose.CaptionTrack ? "text/vtt" : "video/mp4",
            State = MediaSourceState.Pending,
            ProviderMode = ProviderMode.Deterministic,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        };

    /// <summary>Creates the session a source needs, and returns the unsaved source.</summary>
    private async Task<MediaSource> StageSourceAsync(
        DanielsDojoDbContext context,
        string blobName,
        MediaPurpose purpose = MediaPurpose.LessonVideo)
    {
        MediaUploadSession session = Session(blobName);
        session.Purpose = purpose;
        session.Status = MediaUploadStatus.Completed;
        session.CompletedAtUtc = Now;

        context.MediaUploadSessions.Add(session);
        await context.SaveChangesAsync();

        return NewSource(session.Id, blobName, purpose);
    }

    private async Task<MediaSource> AddSourceAsync(
        DanielsDojoDbContext context,
        string blobName,
        MediaSourceState state,
        MediaPurpose purpose = MediaPurpose.LessonVideo)
    {
        MediaSource source = await StageSourceAsync(context, blobName, purpose);
        source.State = state;

        if (state is MediaSourceState.Superseded or MediaSourceState.Archived)
        {
            source.SupersededAtUtc = Now;
        }

        context.MediaSources.Add(source);
        await context.SaveChangesAsync();

        return source;
    }

    private static MediaCaptionTrack Track(Guid lessonVideoId, Guid mediaSourceId, string languageCode) => new()
    {
        Id = Guid.NewGuid(),
        LessonVideoId = lessonVideoId,
        MediaSourceId = mediaSourceId,
        LanguageCode = languageCode,
        DisplayName = languageCode.ToUpperInvariant(),
        Status = LessonVideoStatus.Requested,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    private static async Task AssertViolationAsync(
        DanielsDojoDbContext context,
        string expectedConstraintName)
    {
        DbUpdateException exception =
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains(
            expectedConstraintName,
            exception.InnerException?.Message ?? string.Empty,
            StringComparison.Ordinal);
    }
}
