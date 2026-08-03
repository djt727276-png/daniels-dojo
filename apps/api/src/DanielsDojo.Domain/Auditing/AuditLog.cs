namespace DanielsDojo.Domain.Auditing;

/// <summary>
/// Append-only record of a security- or money-relevant action. There is deliberately no
/// business update or delete path for audit rows. Never stores secrets, tokens, card
/// data, or raw provider payloads — <see cref="MetadataJson"/> is redacted and bounded.
/// </summary>
public sealed class AuditLog
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Acting user, or null for system-initiated actions.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Action name, for example "Entitlement.Revoked".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Type of the affected record, for example "Entitlement".</summary>
    public string TargetType { get; set; } = string.Empty;

    /// <summary>Identifier of the affected record.</summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// Why the action was taken. Optional at the storage layer; later phases tighten this
    /// to required for the specific actions that demand justification.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>When the action occurred, stored UTC.</summary>
    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>Correlation identifier tying the row to a request or provider event.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>Optional redacted, size-limited JSON detail. Never a raw provider payload.</summary>
    public string? MetadataJson { get; set; }
}
