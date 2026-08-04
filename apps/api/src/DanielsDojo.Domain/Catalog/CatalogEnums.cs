namespace DanielsDojo.Domain.Catalog;

/// <summary>Difficulty banding shown on a course.</summary>
public enum CourseLevel
{
    /// <summary>Assumes no prior experience.</summary>
    Beginner,

    /// <summary>Assumes working familiarity.</summary>
    Intermediate,

    /// <summary>Assumes deep familiarity.</summary>
    Advanced,

    /// <summary>Suitable for any experience level.</summary>
    AllLevels,
}

/// <summary>
/// Publication state shared by catalog records. Retirement is expressed as
/// <see cref="Archived"/>; there is no global soft-delete flag.
/// </summary>
public enum PublicationStatus
{
    /// <summary>Authoring in progress. Not visible to students.</summary>
    Draft,

    /// <summary>Visible in the catalog.</summary>
    Published,

    /// <summary>Withdrawn but retained for existing purchases and history.</summary>
    Archived,
}

/// <summary>Kind of lesson content.</summary>
public enum LessonType
{
    /// <summary>Video lesson backed by a <see cref="LessonVideo"/> record.</summary>
    Video,

    /// <summary>Written lesson backed by markdown body content.</summary>
    Article,
}

/// <summary>
/// Processing state of a lesson's video, from the moment an upload is authorised to the moment
/// the asset is withdrawn.
/// </summary>
/// <remarks>
/// The vocabulary follows the actual pipeline rather than a generic ready/not-ready flag,
/// because the states fail differently and an operator needs to know which one they are looking
/// at: bytes that never arrived, bytes that arrived but were rejected, and an asset the
/// provider could not process are three separate problems with three separate fixes.
/// </remarks>
public enum LessonVideoStatus
{
    /// <summary>An upload has been authorised; no bytes have arrived.</summary>
    Requested,

    /// <summary>The client is writing blocks to storage.</summary>
    Uploading,

    /// <summary>The exact source master is stored and its properties are verified.</summary>
    AzureStored,

    /// <summary>The processing provider has been handed the stored object.</summary>
    MuxIngesting,

    /// <summary>The provider is transcoding.</summary>
    Processing,

    /// <summary>Playable.</summary>
    Ready,

    /// <summary>Upload, verification, or processing failed; see the failure code.</summary>
    Failed,

    /// <summary>
    /// A replacement is in flight. The previous asset stays the one being served until the new
    /// one is verified, so a bad re-upload never takes a working lesson off the air.
    /// </summary>
    Replacing,

    /// <summary>Deliberately withdrawn by an administrator. Records are retained.</summary>
    Archived,
}
