using DanielsDojo.Application.Common;

namespace DanielsDojo.Application.Privacy;

/// <summary>Everything the platform holds about one member, in portable form.</summary>
public sealed record PersonalDataExport(
    DateTimeOffset GeneratedAtUtc,
    ExportedAccount Account,
    ExportedCommunityProfile? CommunityProfile,
    IReadOnlyList<ExportedFriend> Friends,
    IReadOnlyList<ExportedMessage> MessagesSent,
    IReadOnlyList<ExportedForumPost> ForumPosts,
    IReadOnlyList<ExportedReview> Reviews,
    IReadOnlyList<ExportedEnrollment> Enrollments,
    IReadOnlyList<ExportedCertificate> Certificates,
    IReadOnlyList<ExportedOrder> Orders);

/// <summary>The account itself.</summary>
public sealed record ExportedAccount(
    string DisplayName,
    string Email,
    bool EmailVerified,
    DateTimeOffset CreatedAtUtc);

/// <summary>The community profile and its settings.</summary>
public sealed record ExportedCommunityProfile(
    string Handle,
    string? Bio,
    bool HasAvatar,
    bool IsDiscoverable,
    string FriendRequestPolicy,
    string MessagePolicy,
    DateTimeOffset CreatedAtUtc);

/// <summary>An accepted friendship.</summary>
public sealed record ExportedFriend(string Handle, DateTimeOffset AcceptedAtUtc);

/// <summary>A direct message the member sent. Received messages belong to their author.</summary>
public sealed record ExportedMessage(
    string ToHandle,
    string Body,
    string Status,
    DateTimeOffset SentAtUtc);

/// <summary>A forum contribution.</summary>
public sealed record ExportedForumPost(
    string ThreadTitle,
    string Body,
    string Status,
    DateTimeOffset PostedAtUtc);

/// <summary>A course review.</summary>
public sealed record ExportedReview(
    string CourseTitle,
    int Rating,
    string Body,
    string Status,
    DateTimeOffset WrittenAtUtc);

/// <summary>A course enrollment with overall progress.</summary>
public sealed record ExportedEnrollment(
    string CourseTitle,
    DateTimeOffset EnrolledAtUtc,
    int LessonsCompleted);

/// <summary>An issued certificate.</summary>
public sealed record ExportedCertificate(
    string CourseTitle,
    string SerialNumber,
    string Status,
    DateTimeOffset IssuedAtUtc);

/// <summary>A commerce order, kept at summary level.</summary>
public sealed record ExportedOrder(
    Guid OrderId,
    string Status,
    long TotalMinor,
    string Currency,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// The member's rights over their own data: seeing all of it, and leaving with it gone.
/// </summary>
/// <remarks>
/// <para>
/// Export returns only data belonging to the requesting member — messages they wrote, not
/// ones they received; their own progress, never anyone else's. Deletion is the documented
/// lifecycle: community presence is erased, sent messages are tombstoned, authored forum
/// content survives unattributed, and financial records are retained as law requires. The
/// external-subject binding is scrubbed, so the person can register again from nothing.
/// </para>
/// <para>Both operations are audited.</para>
/// </remarks>
public interface IPrivacyService
{
    /// <summary>Collects everything held about the member.</summary>
    Task<PersonalDataExport> ExportAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the member's account per the retention policy. Irreversible.
    /// </summary>
    Task<OperationResult> DeleteAccountAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
