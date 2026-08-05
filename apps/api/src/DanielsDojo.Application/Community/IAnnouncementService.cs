using DanielsDojo.Application.Common;

namespace DanielsDojo.Application.Community;

/// <summary>Posts a course announcement.</summary>
/// <param name="Title">Thread title members will see.</param>
/// <param name="Body">Plain-text body. Never rendered as markup anywhere.</param>
public sealed record PostAnnouncementRequest(string Title, string Body);

/// <summary>What posting an announcement produced.</summary>
/// <param name="ThreadId">The announcement thread members are pointed at.</param>
/// <param name="MembersNotified">How many enrolled members received a notification.</param>
public sealed record AnnouncementPosted(Guid ThreadId, int MembersNotified);

/// <summary>
/// Course announcements: an administrator speaks once, every enrolled member hears about it.
/// </summary>
/// <remarks>
/// An announcement is an ordinary pinned forum thread in the reserved "announcements"
/// category — so replies, moderation, plain-text rules, and block handling all come from the
/// forum for free — plus a notification fan-out to the members enrolled in the course. The
/// notification carries a pointer, never content, like every other notification.
/// </remarks>
public interface IAnnouncementService
{
    /// <summary>Posts an announcement for one course and notifies its enrolled members.</summary>
    Task<OperationResult<AnnouncementPosted>> PostAsync(
        Guid actorUserId,
        Guid courseId,
        PostAnnouncementRequest request,
        CancellationToken cancellationToken = default);
}
