using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Learning;

/// <summary>
/// A course-completion certificate.
/// </summary>
/// <remarks>
/// Issued only by the progress pipeline when every published lesson of the course is
/// complete — there is no path that writes one directly from a request. The verification
/// code is the public handle: random, unguessable, and safe to print, it lets anyone confirm
/// the certificate without exposing the holder's account. Revocation marks rather than
/// deletes, so a revoked code verifies as revoked instead of vanishing.
/// </remarks>
public sealed class Certificate
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>The member the certificate was issued to.</summary>
    public Guid UserId { get; set; }

    /// <summary>The completed course.</summary>
    public Guid CourseId { get; set; }

    /// <summary>
    /// Public verification code, unique and unguessable. What the certificate displays and
    /// what the verification page looks up.
    /// </summary>
    public string VerificationCode { get; set; } = string.Empty;

    /// <summary>Course title captured at issuance, so later edits never rewrite history.</summary>
    public string CourseTitleAtIssue { get; set; } = string.Empty;

    /// <summary>Holder display name captured at issuance.</summary>
    public string HolderNameAtIssue { get; set; } = string.Empty;

    /// <summary>When the certificate was earned.</summary>
    public DateTimeOffset IssuedAtUtc { get; set; }

    /// <summary>When an administrator revoked it, if ever.</summary>
    public DateTimeOffset? RevokedAtUtc { get; set; }

    /// <summary>Why it was revoked. Required when revoked.</summary>
    public string? RevocationReason { get; set; }

    /// <summary>Row creation instant.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Optimistic concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The holder.</summary>
    public User? User { get; set; }

    /// <summary>The course.</summary>
    public Course? Course { get; set; }

    /// <summary>Whether the certificate currently verifies as valid.</summary>
    public bool IsValid => RevokedAtUtc is null;
}
