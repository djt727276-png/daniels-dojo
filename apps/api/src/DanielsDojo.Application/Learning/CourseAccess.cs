namespace DanielsDojo.Application.Learning;

/// <summary>
/// Why a viewer may or may not see protected course content.
/// </summary>
/// <remarks>
/// Deliberately not a boolean. The reasons behave differently — a membership ends at a period
/// boundary, a lifetime purchase does not end at all, an Admin preview must never be mistaken
/// for a purchase, and a Development grant must never exist outside Development — so collapsing
/// them into "has access" would make every one of those rules unenforceable at the call site.
/// </remarks>
public enum CourseAccessReason
{
    /// <summary>No access.</summary>
    None = 0,

    /// <summary>An administrator reviewing unpublished work.</summary>
    AdminPreview,

    /// <summary>An explicitly published free preview lesson.</summary>
    PublicPreview,

    /// <summary>The seeded Development student, in the exact Development environment only.</summary>
    DevelopmentGrant,

    /// <summary>A non-expiring purchase of this specific course.</summary>
    Lifetime,

    /// <summary>An active membership that covers this course.</summary>
    Membership,

    /// <summary>An administrator-issued complimentary grant.</summary>
    ManualGrant,
}

/// <summary>
/// Why access was refused, in terms a client may safely display.
/// </summary>
/// <remarks>
/// These never distinguish "you had it and lost it" from "you never had it" in a way that
/// discloses somebody else's purchase, and they carry no provider state.
/// </remarks>
public enum CourseAccessDenial
{
    /// <summary>Access was granted.</summary>
    None = 0,

    /// <summary>Nobody is signed in and the content is not a public preview.</summary>
    AuthenticationRequired,

    /// <summary>Signed in, but nothing grants this course.</summary>
    PurchaseRequired,

    /// <summary>A grant existed and its period has ended.</summary>
    Expired,

    /// <summary>A grant existed and was withdrawn.</summary>
    Revoked,

    /// <summary>The course, section, or lesson is not published.</summary>
    NotPublished,

    /// <summary>The lesson's media is not playable yet.</summary>
    MediaNotReady,

    /// <summary>The target does not exist, or the caller may not know that it does.</summary>
    NotFound,
}

/// <summary>
/// One access decision.
/// </summary>
/// <param name="Granted">Whether the viewer may be served the content.</param>
/// <param name="Reason">What granted it.</param>
/// <param name="Denial">Why it was refused, when it was.</param>
/// <param name="EndsAtUtc">When the granting source stops covering the course, if it ever does.</param>
/// <param name="IsPreviewOnly">
/// True when only explicitly previewable lessons are covered, so a caller cannot mistake a
/// free preview for full course access.
/// </param>
public sealed record CourseAccess(
    bool Granted,
    CourseAccessReason Reason,
    CourseAccessDenial Denial,
    DateTimeOffset? EndsAtUtc,
    bool IsPreviewOnly)
{
    /// <summary>A refusal carrying no detail beyond its category.</summary>
    public static CourseAccess Deny(CourseAccessDenial denial) =>
        new(false, CourseAccessReason.None, denial, null, false);

    /// <summary>A grant from a durable or time-bounded source.</summary>
    public static CourseAccess Allow(
        CourseAccessReason reason,
        DateTimeOffset? endsAtUtc = null,
        bool previewOnly = false) =>
        new(true, reason, CourseAccessDenial.None, endsAtUtc, previewOnly);

    /// <summary>
    /// Stable code for the client, for example <c>access.purchase_required</c>. Clients branch
    /// on this rather than on wording.
    /// </summary>
    public string Code => Granted
        ? $"access.granted.{Reason.ToString().ToLowerInvariant()}"
        : $"access.denied.{Denial.ToString().ToLowerInvariant()}";

    /// <summary>Whether resources attached to the course may be downloaded.</summary>
    /// <remarks>
    /// A free preview deliberately does not carry downloads: the preview exists to show what
    /// the course is like, not to hand out its materials.
    /// </remarks>
    public bool AllowsResourceDownload => Granted && !IsPreviewOnly;
}

/// <summary>
/// The single authority on whether a viewer may be served protected course content.
/// </summary>
/// <remarks>
/// Every path that could reveal content consults this: the learning projection, playback-token
/// issuance, resource downloads, enrollment and progress writes, and My Learning. One
/// implementation means a later change — a new grant type, a stricter rule — lands everywhere
/// at once instead of in whichever call sites somebody remembered.
/// </remarks>
public interface ICourseAccessEvaluator
{
    /// <summary>Decides access to a course as a whole.</summary>
    /// <param name="userId">The signed-in member, or null for an anonymous viewer.</param>
    Task<CourseAccess> EvaluateCourseAsync(
        Guid? userId,
        Guid courseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decides access to one lesson, additionally applying publication and preview rules that
    /// only make sense at lesson level.
    /// </summary>
    Task<CourseAccess> EvaluateLessonAsync(
        Guid? userId,
        Guid lessonId,
        CancellationToken cancellationToken = default);

    /// <summary>Course identifiers the member currently holds access to, for My Learning.</summary>
    Task<IReadOnlyList<Guid>> ListAccessibleCourseIdsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
