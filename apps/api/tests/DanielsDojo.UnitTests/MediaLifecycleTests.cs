using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Media;
using Xunit;

namespace DanielsDojo.UnitTests;

/// <summary>
/// The media transition table, which is what protects a working lesson from a late or
/// duplicated provider event.
/// </summary>
public sealed class MediaLifecycleTests
{
    [Theory]
    [InlineData(LessonVideoStatus.Requested, LessonVideoStatus.Uploading)]
    [InlineData(LessonVideoStatus.Uploading, LessonVideoStatus.AzureStored)]
    [InlineData(LessonVideoStatus.AzureStored, LessonVideoStatus.MuxIngesting)]
    [InlineData(LessonVideoStatus.MuxIngesting, LessonVideoStatus.Processing)]
    [InlineData(LessonVideoStatus.Processing, LessonVideoStatus.Ready)]
    [InlineData(LessonVideoStatus.Ready, LessonVideoStatus.Replacing)]
    [InlineData(LessonVideoStatus.Replacing, LessonVideoStatus.Ready)]
    [InlineData(LessonVideoStatus.Replacing, LessonVideoStatus.Failed)]
    [InlineData(LessonVideoStatus.Failed, LessonVideoStatus.Requested)]
    public void TheHappyPathAndItsRetriesAreAllowed(LessonVideoStatus from, LessonVideoStatus to) =>
        Assert.True(MediaLifecycle.CanTransition(from, to));

    [Theory]
    [InlineData(LessonVideoStatus.Ready, LessonVideoStatus.Processing)]
    [InlineData(LessonVideoStatus.Ready, LessonVideoStatus.MuxIngesting)]
    [InlineData(LessonVideoStatus.Ready, LessonVideoStatus.Uploading)]
    [InlineData(LessonVideoStatus.Ready, LessonVideoStatus.AzureStored)]
    public void AReadyLessonIsNeverDraggedBackwards(LessonVideoStatus from, LessonVideoStatus to)
    {
        // A duplicate "processing" webhook arriving after the real "ready" one must not take a
        // working lesson off the air.
        Assert.False(MediaLifecycle.CanTransition(from, to));
    }

    [Fact]
    public void ReplacingIsOnlyReachableFromReady()
    {
        foreach (LessonVideoStatus status in Enum.GetValues<LessonVideoStatus>())
        {
            bool expected = status == LessonVideoStatus.Ready;

            Assert.Equal(expected, MediaLifecycle.CanTransition(status, LessonVideoStatus.Replacing));
        }
    }

    [Fact]
    public void ArchivingIsAvailableFromAnywhereAndIsOneWay()
    {
        foreach (LessonVideoStatus status in Enum.GetValues<LessonVideoStatus>())
        {
            if (status == LessonVideoStatus.Archived)
            {
                continue;
            }

            Assert.True(MediaLifecycle.CanTransition(status, LessonVideoStatus.Archived));
        }

        Assert.Empty(MediaLifecycle.AllowedTargets(LessonVideoStatus.Archived));
        Assert.True(MediaLifecycle.IsTerminal(LessonVideoStatus.Archived));
    }

    [Fact]
    public void AReplacingLessonStillCountsAsPlayable()
    {
        // The whole point of last-known-good: while a replacement is in flight the previous
        // asset keeps serving.
        Assert.True(MediaLifecycle.IsPlayable(LessonVideoStatus.Ready));
        Assert.True(MediaLifecycle.IsPlayable(LessonVideoStatus.Replacing));

        Assert.False(MediaLifecycle.IsPlayable(LessonVideoStatus.Processing));
        Assert.False(MediaLifecycle.IsPlayable(LessonVideoStatus.Failed));
        Assert.False(MediaLifecycle.IsPlayable(LessonVideoStatus.Archived));
    }

    [Fact]
    public void AnOlderProviderEventIsRecognisedAsStale()
    {
        DateTimeOffset applied = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

        Assert.False(MediaLifecycle.IsNewerThan(applied.AddSeconds(-1), applied));
        Assert.False(MediaLifecycle.IsNewerThan(applied, applied));
        Assert.True(MediaLifecycle.IsNewerThan(applied.AddSeconds(1), applied));

        // Nothing has been applied yet, so the first event always wins.
        Assert.True(MediaLifecycle.IsNewerThan(applied, null));
    }

    [Fact]
    public void ASessionIsOnlyOpenWhileItIsUnfinishedAndUnexpired()
    {
        DateTimeOffset now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

        var open = new MediaUploadSession
        {
            Status = MediaUploadStatus.Requested,
            ExpiresAtUtc = now.AddMinutes(5),
        };
        Assert.True(open.IsOpenAt(now));

        var expired = new MediaUploadSession
        {
            Status = MediaUploadStatus.Uploading,
            ExpiresAtUtc = now.AddMinutes(-1),
        };
        Assert.False(expired.IsOpenAt(now));

        var finished = new MediaUploadSession
        {
            Status = MediaUploadStatus.Completed,
            ExpiresAtUtc = now.AddMinutes(5),
        };
        Assert.False(finished.IsOpenAt(now));
    }

    [Fact]
    public void CloudVerificationNeedsPropertiesRestoreAndAMatchingLength()
    {
        DateTimeOffset now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

        var source = new MediaSource { ContentLength = 1024 };
        Assert.False(source.IsCloudVerified);

        source.PropertiesVerifiedAtUtc = now;
        Assert.False(source.IsCloudVerified);

        source.RestoreVerifiedAtUtc = now;
        source.RestoreVerifiedLength = 999;

        // A restore that produced a different number of bytes is not a verification.
        Assert.False(source.IsCloudVerified);

        source.RestoreVerifiedLength = 1024;
        Assert.True(source.IsCloudVerified);
    }
}
