namespace DanielsDojo.Application.Learning;

/// <summary>One lesson as a learner sees it in the curriculum.</summary>
/// <param name="Id">Lesson identifier.</param>
/// <param name="Slug">URL segment.</param>
/// <param name="Title">Lesson title.</param>
/// <param name="LessonType">Video or Article.</param>
/// <param name="SortOrder">Position within its section.</param>
/// <param name="IsPreview">Whether it is an explicitly published free preview.</param>
/// <param name="EstimatedDurationSeconds">Authored estimate, for the outline.</param>
/// <param name="IsAccessible">
/// Whether this viewer may open it. Decided server-side by the access evaluator; the client
/// renders a lock from this rather than deciding for itself.
/// </param>
/// <param name="IsPlayable">Whether media is ready, for a video lesson.</param>
/// <param name="StartedAtUtc">When this viewer first opened it.</param>
/// <param name="CompletedAtUtc">When this viewer completed it.</param>
/// <param name="LastPositionSeconds">Where to resume.</param>
public sealed record CurriculumLesson(
    Guid Id,
    string Slug,
    string Title,
    string LessonType,
    int SortOrder,
    bool IsPreview,
    int? EstimatedDurationSeconds,
    bool IsAccessible,
    bool IsPlayable,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int LastPositionSeconds);

/// <summary>One published section and its lessons.</summary>
/// <param name="Id">Section identifier.</param>
/// <param name="Title">Section title.</param>
/// <param name="SortOrder">Position within the course.</param>
/// <param name="Lessons">Published lessons, in order.</param>
public sealed record CurriculumSection(
    Guid Id,
    string Title,
    int SortOrder,
    IReadOnlyList<CurriculumLesson> Lessons);

/// <summary>A course outline with this viewer's progress folded in.</summary>
/// <param name="CourseId">Course identifier.</param>
/// <param name="Slug">URL segment.</param>
/// <param name="Title">Course title.</param>
/// <param name="Summary">Short description.</param>
/// <param name="Sections">Published sections, in order.</param>
/// <param name="AccessGranted">Whether the viewer holds full access.</param>
/// <param name="AccessReason">What granted it, or <c>None</c>.</param>
/// <param name="AccessDenial">Why it was refused, or <c>None</c>.</param>
/// <param name="AccessCode">Stable code the client branches on.</param>
/// <param name="AccessEndsAtUtc">When the granting source stops covering the course.</param>
/// <param name="IsPreviewOnly">True when only preview lessons are open.</param>
/// <param name="TotalLessons">Published lesson count.</param>
/// <param name="CompletedLessons">How many this viewer has completed.</param>
/// <param name="ResumeLessonId">
/// Where "continue" should go: the first incomplete accessible lesson, or the last one
/// completed if everything is done.
/// </param>
public sealed record CourseCurriculum(
    Guid CourseId,
    string Slug,
    string Title,
    string Summary,
    IReadOnlyList<CurriculumSection> Sections,
    bool AccessGranted,
    string AccessReason,
    string AccessDenial,
    string AccessCode,
    DateTimeOffset? AccessEndsAtUtc,
    bool IsPreviewOnly,
    int TotalLessons,
    int CompletedLessons,
    Guid? ResumeLessonId);

/// <summary>A downloadable file attached to a lesson.</summary>
/// <param name="Id">Resource identifier.</param>
/// <param name="DisplayName">Name shown to the learner.</param>
/// <param name="MediaType">Content type.</param>
/// <param name="SizeBytes">Size, for the download affordance.</param>
public sealed record LessonResourceLink(Guid Id, string DisplayName, string MediaType, long? SizeBytes);

/// <summary>One lesson opened for study.</summary>
/// <param name="Id">Lesson identifier.</param>
/// <param name="CourseId">Owning course.</param>
/// <param name="CourseSlug">Owning course slug, for navigation.</param>
/// <param name="Title">Lesson title.</param>
/// <param name="LessonType">Video or Article.</param>
/// <param name="BodyMarkdown">Article body, when this is an article.</param>
/// <param name="IsPlayable">Whether a video is ready to play.</param>
/// <param name="Resources">
/// Attached files. Empty for a preview viewer — a preview shows what the course is like, it
/// does not hand out the materials.
/// </param>
/// <param name="PreviousLessonId">Previous accessible lesson, for navigation.</param>
/// <param name="NextLessonId">Next accessible lesson, for navigation.</param>
/// <param name="StartedAtUtc">When this viewer first opened it.</param>
/// <param name="CompletedAtUtc">When this viewer completed it.</param>
/// <param name="LastPositionSeconds">Where to resume.</param>
/// <param name="AccessReason">What granted access.</param>
public sealed record LessonDetail(
    Guid Id,
    Guid CourseId,
    string CourseSlug,
    string Title,
    string LessonType,
    string? BodyMarkdown,
    bool IsPlayable,
    IReadOnlyList<LessonResourceLink> Resources,
    Guid? PreviousLessonId,
    Guid? NextLessonId,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int LastPositionSeconds,
    string AccessReason);

/// <summary>One course on the My Learning shelf.</summary>
/// <param name="CourseId">Course identifier.</param>
/// <param name="Slug">URL segment.</param>
/// <param name="Title">Course title.</param>
/// <param name="Summary">Short description.</param>
/// <param name="TotalLessons">Published lesson count.</param>
/// <param name="CompletedLessons">How many this learner has completed.</param>
/// <param name="PercentComplete">Rounded completion percentage.</param>
/// <param name="ResumeLessonId">Where "continue" goes.</param>
/// <param name="LastAccessedAtUtc">When the learner last opened the course.</param>
/// <param name="AccessReason">What currently grants it, for the UI to explain.</param>
/// <param name="AccessEndsAtUtc">When that grant runs out, if it ever does.</param>
public sealed record MyLearningCourse(
    Guid CourseId,
    string Slug,
    string Title,
    string Summary,
    int TotalLessons,
    int CompletedLessons,
    int PercentComplete,
    Guid? ResumeLessonId,
    DateTimeOffset? LastAccessedAtUtc,
    string AccessReason,
    DateTimeOffset? AccessEndsAtUtc);

/// <summary>A learner reporting where they got to.</summary>
/// <param name="PositionSeconds">Playback position, or zero for an article.</param>
/// <param name="Completed">
/// Whether the lesson is finished. Completion is a one-way latch — reporting false never
/// un-completes a lesson somebody already finished, because that would silently erase progress
/// on a stray request from a stale tab.
/// </param>
public sealed record ProgressUpdate(int PositionSeconds, bool Completed);

/// <summary>The recorded outcome of a progress report.</summary>
/// <param name="LessonId">Lesson the progress belongs to.</param>
/// <param name="StartedAtUtc">When the learner first opened it.</param>
/// <param name="CompletedAtUtc">When it was completed, if it has been.</param>
/// <param name="LastPositionSeconds">The stored resume position.</param>
/// <param name="CourseCompleted">Whether that completion finished the whole course.</param>
/// <param name="CompletedLessons">Completed lessons in the course after this update.</param>
/// <param name="TotalLessons">Published lessons in the course.</param>
public sealed record ProgressRecorded(
    Guid LessonId,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int LastPositionSeconds,
    bool CourseCompleted,
    int CompletedLessons,
    int TotalLessons);

/// <summary>One certificate as its holder sees it.</summary>
/// <param name="Id">Certificate identifier.</param>
/// <param name="CourseId">The completed course.</param>
/// <param name="CourseTitle">Course title captured at issuance.</param>
/// <param name="HolderName">Holder name captured at issuance.</param>
/// <param name="VerificationCode">Public code printed on the certificate.</param>
/// <param name="IssuedAtUtc">When it was earned.</param>
/// <param name="IsValid">False once revoked.</param>
public sealed record CertificateView(
    Guid Id,
    Guid CourseId,
    string CourseTitle,
    string HolderName,
    string VerificationCode,
    DateTimeOffset IssuedAtUtc,
    bool IsValid);

/// <summary>
/// What anyone may learn from a verification code.
/// </summary>
/// <remarks>
/// Deliberately only what a certificate itself displays: the holder's name as issued, the
/// course, the date, and validity. No account identifiers, no email, no progress detail.
/// </remarks>
/// <param name="CourseTitle">Course title at issuance.</param>
/// <param name="HolderName">Holder name at issuance.</param>
/// <param name="IssuedAtUtc">When it was earned.</param>
/// <param name="IsValid">Whether it currently verifies.</param>
/// <param name="RevokedAtUtc">When it stopped verifying, when it did.</param>
public sealed record CertificateVerification(
    string CourseTitle,
    string HolderName,
    DateTimeOffset IssuedAtUtc,
    bool IsValid,
    DateTimeOffset? RevokedAtUtc);
