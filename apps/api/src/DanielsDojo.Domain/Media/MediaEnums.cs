namespace DanielsDojo.Domain.Media;

/// <summary>
/// How a provider adapter is wired for this process.
/// </summary>
/// <remarks>
/// Chosen explicitly by configuration and recorded on the rows a provider produced, so a
/// deterministic row can never later be mistaken for a real one. Nothing infers a provider
/// from whether a key happens to be present.
/// </remarks>
public enum ProviderMode
{
    /// <summary>The capability is switched off. Every operation refuses.</summary>
    Disabled,

    /// <summary>A local, reproducible stand-in that makes no network call.</summary>
    Deterministic,

    /// <summary>The genuine provider SDK against configured credentials.</summary>
    Real,
}

/// <summary>What a piece of uploaded media is for.</summary>
public enum MediaPurpose
{
    /// <summary>The exact source master for a video lesson.</summary>
    LessonVideo,

    /// <summary>A downloadable file attached to a lesson.</summary>
    LessonResource,

    /// <summary>A course cover image.</summary>
    CourseImage,

    /// <summary>A caption or subtitle track for a video lesson.</summary>
    CaptionTrack,

    /// <summary>A community profile avatar.</summary>
    Avatar,
}

/// <summary>
/// Lifecycle of one upload attempt.
/// </summary>
/// <remarks>
/// An attempt is a short-lived authorisation to write exactly one blob. It is deliberately
/// separate from the media it produces, so an abandoned or expired attempt leaves no trace on
/// the lesson and a replacement cannot disturb what is currently serving.
/// </remarks>
public enum MediaUploadStatus
{
    /// <summary>Authorised; no bytes have been written yet.</summary>
    Requested,

    /// <summary>The client reported that it began writing blocks.</summary>
    Uploading,

    /// <summary>Finalised and verified against the trusted blob properties.</summary>
    Completed,

    /// <summary>The authorisation window closed before finalisation.</summary>
    Expired,

    /// <summary>Withdrawn by the operator before finalisation.</summary>
    Cancelled,

    /// <summary>Finalisation ran but the stored object did not match what was declared.</summary>
    Failed,
}

/// <summary>
/// Whether a stored source object is the one currently in use.
/// </summary>
/// <remarks>
/// A replacement is only promoted once it has been verified end to end, so the previous
/// master stays <see cref="Current"/> until then. That is what makes "last known good" real
/// rather than a hope.
/// </remarks>
public enum MediaSourceState
{
    /// <summary>Uploaded and verified, but not yet promoted over an existing source.</summary>
    Pending,

    /// <summary>The source the platform is serving and protecting.</summary>
    Current,

    /// <summary>Replaced by a newer verified source. Retained, never deleted.</summary>
    Superseded,

    /// <summary>Withdrawn from use by an operator. Retained.</summary>
    Archived,
}

/// <summary>
/// One step of the evidence a human needs before deleting an original from their own machine.
/// </summary>
/// <remarks>
/// Every step is recorded separately because they fail independently: a blob can exist with the
/// wrong length, a restore can succeed while playback does not, and only a person can confirm
/// that the beginning, middle, and end of the video are actually watchable.
/// </remarks>
public enum MediaVerificationStep
{
    /// <summary>Blob properties matched the declared identity, length, and integrity hash.</summary>
    CloudProperties,

    /// <summary>A streamed restore of that exact blob version matched the source checksum.</summary>
    RestoreChecksum,

    /// <summary>The processing provider reported the asset playable.</summary>
    ProviderReady,

    /// <summary>An authorised Admin played the protected stream.</summary>
    AdminPlayback,

    /// <summary>An authorised Student played the published protected stream.</summary>
    StudentPlayback,

    /// <summary>A person confirmed the beginning, middle, and end are watchable.</summary>
    HumanSpotCheck,
}
