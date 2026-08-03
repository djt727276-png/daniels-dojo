namespace DanielsDojo.Domain.Commerce;

/// <summary>
/// Idempotency and processing record for one inbound provider event. The complete raw
/// payload is deliberately never stored — only a SHA-256 digest for correlation, plus a
/// bounded redacted error string.
/// </summary>
public sealed class WebhookEvent
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Provider name, for example "Stripe".</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Provider event identifier. Unique per provider; the idempotency key.</summary>
    public string ExternalEventId { get; set; } = string.Empty;

    /// <summary>Provider event type, for example "invoice.paid".</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Processing state.</summary>
    public WebhookEventStatus Status { get; set; } = WebhookEventStatus.Received;

    /// <summary>How many processing attempts have been made.</summary>
    public int AttemptCount { get; set; }

    /// <summary>When the event was received, stored UTC.</summary>
    public DateTimeOffset ReceivedAtUtc { get; set; }

    /// <summary>When processing completed, stored UTC.</summary>
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    /// <summary>When the next retry is due, stored UTC.</summary>
    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    /// <summary>Bounded, redacted failure detail. Never a raw payload or secret.</summary>
    public string? LastError { get; set; }

    /// <summary>Hex SHA-256 digest of the received payload, for correlation only.</summary>
    public string PayloadSha256 { get; set; } = string.Empty;
}
