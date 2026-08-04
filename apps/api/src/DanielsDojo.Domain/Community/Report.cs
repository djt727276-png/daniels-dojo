using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Community;

/// <summary>
/// A member's report about a profile, thread, post, or message.
/// </summary>
/// <remarks>
/// A reporter may hold at most one open report per target, which is enforced by a filtered
/// unique index rather than by application code so a double submission cannot flood the queue.
/// <para>
/// A report is also what unlocks moderator access to a reported private message: there is no
/// endpoint that lists arbitrary conversations, so review is always scoped to a specific
/// reported target and is itself audited.
/// </para>
/// </remarks>
public sealed class Report
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Member who submitted the report.</summary>
    public Guid ReporterUserId { get; set; }

    /// <summary>What kind of thing was reported.</summary>
    public ReportTargetType TargetType { get; set; }

    /// <summary>Identifier of the reported thing.</summary>
    public Guid TargetId { get; set; }

    /// <summary>Why it was reported.</summary>
    public ReportReasonCode ReasonCode { get; set; }

    /// <summary>Optional short elaboration from the reporter.</summary>
    public string? Detail { get; set; }

    /// <summary>Lifecycle state.</summary>
    public ReportStatus Status { get; set; } = ReportStatus.Open;

    /// <summary>Moderator handling the report.</summary>
    public Guid? HandledByUserId { get; set; }

    /// <summary>Moderator's recorded outcome note.</summary>
    public string? Resolution { get; set; }

    /// <summary>When the report was resolved or dismissed, stored UTC.</summary>
    public DateTimeOffset? HandledAtUtc { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The reporter.</summary>
    public User? Reporter { get; set; }

    /// <summary>The moderator who handled it.</summary>
    public User? HandledByUser { get; set; }

    /// <summary>Whether the report is still awaiting a decision.</summary>
    public bool IsOpen => Status is ReportStatus.Open or ReportStatus.Reviewing;
}
