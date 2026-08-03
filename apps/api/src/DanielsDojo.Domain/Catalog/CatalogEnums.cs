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

/// <summary>Processing state of a lesson's video asset at the video provider.</summary>
public enum LessonVideoStatus
{
    /// <summary>No asset has been submitted yet.</summary>
    Pending,

    /// <summary>The provider is processing the asset.</summary>
    Preparing,

    /// <summary>Playable.</summary>
    Ready,

    /// <summary>Processing failed; see the failure code.</summary>
    Errored,

    /// <summary>Deliberately disabled by an administrator.</summary>
    Disabled,
}
