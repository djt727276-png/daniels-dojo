using DanielsDojo.Application.Media;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Media;
using DanielsDojo.Infrastructure.Persistence;

namespace DanielsDojo.Infrastructure.Media;

/// <summary>
/// The one place provider state becomes lesson state.
/// </summary>
/// <remarks>
/// <para>
/// A notification and a reconciliation pass must reach identical conclusions from identical
/// provider state, so both go through here. Anything that would move a lesson backwards is
/// refused: the transition table decides, and a lesson that is already playable stays playable.
/// </para>
/// <para>
/// Nothing here saves. The caller owns the transaction, which keeps the state change and its
/// audit row in the same write.
/// </para>
/// </remarks>
internal sealed class VideoStateMachine(DanielsDojoDbContext context)
{
    /// <summary>Applies provider state to a video. Returns whether anything changed.</summary>
    public bool Apply(LessonVideo record, VideoAssetState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(state);

        bool replacing = record.Status == LessonVideoStatus.Replacing;

        if (state.Status == LessonVideoStatus.Ready)
        {
            if (state.PlaybackId is not { Length: > 0 })
            {
                // Ready with nothing to play is not ready. Waiting for the next notification is
                // better than writing a row the schema would rightly reject.
                return false;
            }

            if (record.Status == LessonVideoStatus.Ready
                && record.MuxPlaybackId == state.PlaybackId)
            {
                return false;
            }

            record.MuxAssetId = state.AssetId;
            record.MuxPlaybackId = state.PlaybackId;
            record.LastKnownGoodAssetId = state.AssetId;
            record.LastKnownGoodPlaybackId = state.PlaybackId;
            record.DurationSeconds = state.DurationSeconds ?? record.DurationSeconds;
            record.AspectRatio = state.AspectRatio ?? record.AspectRatio;
            record.FailureCode = null;
            record.Status = LessonVideoStatus.Ready;
            record.LastProviderEventAtUtc = now;
            record.UpdatedAtUtc = now;

            PromoteIncomingSource(record, now);

            return true;
        }

        if (state.Status == LessonVideoStatus.Failed)
        {
            if (replacing)
            {
                // A failed replacement puts the lesson back on the video it was already
                // serving, which is why the previous asset was pinned before the upload began.
                record.Status = LessonVideoStatus.Ready;
                record.MuxAssetId = record.LastKnownGoodAssetId;
                record.MuxPlaybackId = record.LastKnownGoodPlaybackId;
                record.IncomingSourceId = null;
                record.FailureCode = null;
                record.LastProviderEventAtUtc = now;
                record.UpdatedAtUtc = now;

                return true;
            }

            if (record.Status == LessonVideoStatus.Failed
                || !MediaLifecycle.CanTransition(record.Status, LessonVideoStatus.Failed))
            {
                return false;
            }

            record.Status = LessonVideoStatus.Failed;
            record.FailureCode = state.FailureCode ?? "provider_errored";
            record.LastProviderEventAtUtc = now;
            record.UpdatedAtUtc = now;

            return true;
        }

        // Intermediate progress. It must never disturb a lesson that is already playable, nor
        // one mid-replacement behind a working asset.
        if (replacing
            || record.Status == state.Status
            || !MediaLifecycle.CanTransition(record.Status, state.Status))
        {
            return false;
        }

        record.Status = state.Status;
        record.LastProviderEventAtUtc = now;
        record.UpdatedAtUtc = now;

        return true;
    }

    /// <summary>
    /// Makes the incoming master current and retires the previous one, in a single step.
    /// </summary>
    /// <remarks>
    /// Retirement happens first because the database permits only one current source per lesson.
    /// Nothing is deleted — the previous master stays exactly where it is, marked superseded,
    /// which is what makes a replacement reversible and what keeps an earlier verified original
    /// safe after somebody has already deleted their local copy of it.
    /// </remarks>
    private void PromoteIncomingSource(LessonVideo record, DateTimeOffset now)
    {
        if (record.IncomingSourceId is not { } incomingId)
        {
            return;
        }

        MediaSource? incoming = Track(incomingId);

        if (incoming is null)
        {
            return;
        }

        if (record.CurrentSourceId is { } previousId
            && previousId != incomingId
            && Track(previousId) is { State: MediaSourceState.Current } previous)
        {
            previous.State = MediaSourceState.Superseded;
            previous.SupersededAtUtc = now;
            previous.UpdatedAtUtc = now;
        }

        incoming.State = MediaSourceState.Current;
        incoming.UpdatedAtUtc = now;

        record.CurrentSourceId = incoming.Id;
        record.IncomingSourceId = null;
    }

    private MediaSource? Track(Guid sourceId) =>
        context.MediaSources.Local.FirstOrDefault(source => source.Id == sourceId)
        ?? context.MediaSources.Find(sourceId);
}
