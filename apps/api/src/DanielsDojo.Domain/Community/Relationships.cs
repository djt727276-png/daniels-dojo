using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Community;

/// <summary>
/// Helpers for the canonical ordering used by every symmetric relationship.
/// </summary>
/// <remarks>
/// Friend requests, friendships, and conversations are all inherently unordered pairs. Storing
/// them as (low, high) lets a single unique index guarantee exactly one row per pair —
/// otherwise (A,B) and (B,A) would both be insertable and the two members could end up with
/// divergent state.
/// <para>
/// Ordering is by the GUID's canonical hex text, not by <see cref="Guid.CompareTo(Guid)"/>.
/// SQL Server orders <c>uniqueidentifier</c> by a different byte precedence than .NET does, so
/// a check constraint written against the native type would disagree with the application for
/// some pairs and reject rows the application considered correctly ordered. Comparing the same
/// hex text on both sides removes that discrepancy: the relative order of two hex strings is
/// identical whether both are upper-case (as SQL Server renders them) or lower-case (as .NET
/// does), so the two comparisons always agree.
/// </para>
/// </remarks>
public static class CanonicalPair
{
    /// <summary>Orders two user identifiers deterministically.</summary>
    public static (Guid Low, Guid High) Order(Guid first, Guid second) =>
        Compare(first, second) <= 0 ? (first, second) : (second, first);

    /// <summary>
    /// Compares two identifiers using the same rule the database check constraints use.
    /// </summary>
    public static int Compare(Guid first, Guid second) =>
        string.CompareOrdinal(first.ToString("D"), second.ToString("D"));

    /// <summary>Whether the pair is valid — two distinct members.</summary>
    public static bool IsValidPair(Guid first, Guid second) => first != second;
}

/// <summary>A pending or resolved request to become friends.</summary>
public sealed class FriendRequest
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Lower half of the canonical pair.</summary>
    public Guid UserLowId { get; set; }

    /// <summary>Upper half of the canonical pair.</summary>
    public Guid UserHighId { get; set; }

    /// <summary>
    /// Which of the two members sent the request. Constrained to be one of the pair, so a
    /// third party can never appear as the requester.
    /// </summary>
    public Guid RequestedByUserId { get; set; }

    /// <summary>Lifecycle state.</summary>
    public FriendRequestStatus Status { get; set; } = FriendRequestStatus.Pending;

    /// <summary>When the request was sent, stored UTC.</summary>
    public DateTimeOffset RequestedAtUtc { get; set; }

    /// <summary>When it was accepted, declined, or cancelled. Stored UTC.</summary>
    public DateTimeOffset? RespondedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Lower member of the pair.</summary>
    public User? UserLow { get; set; }

    /// <summary>Upper member of the pair.</summary>
    public User? UserHigh { get; set; }
}

/// <summary>An accepted, mutual friendship.</summary>
public sealed class Friendship
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Lower half of the canonical pair.</summary>
    public Guid UserLowId { get; set; }

    /// <summary>Upper half of the canonical pair.</summary>
    public Guid UserHighId { get; set; }

    /// <summary>When the friendship began, stored UTC.</summary>
    public DateTimeOffset AcceptedAtUtc { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Lower member of the pair.</summary>
    public User? UserLow { get; set; }

    /// <summary>Upper member of the pair.</summary>
    public User? UserHigh { get; set; }
}

/// <summary>
/// A directed block. Unlike friendship this is deliberately not canonicalised: A blocking B
/// is a different fact from B blocking A, and either one is enough to stop contact.
/// </summary>
public sealed class UserBlock
{
    /// <summary>Member who created the block.</summary>
    public Guid BlockerUserId { get; set; }

    /// <summary>Member who was blocked.</summary>
    public Guid BlockedUserId { get; set; }

    /// <summary>Coarse reason category. No free text is stored.</summary>
    public BlockReasonCategory ReasonCategory { get; set; } = BlockReasonCategory.Unspecified;

    /// <summary>When the block was created, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>The blocking member.</summary>
    public User? Blocker { get; set; }

    /// <summary>The blocked member.</summary>
    public User? Blocked { get; set; }
}
