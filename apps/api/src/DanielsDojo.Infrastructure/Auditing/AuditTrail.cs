using System.Text.Json;
using DanielsDojo.Application.Common;
using DanielsDojo.Domain.Auditing;
using DanielsDojo.Infrastructure.Persistence;

namespace DanielsDojo.Infrastructure.Auditing;

/// <summary>
/// Appends audit rows to the same <see cref="DanielsDojoDbContext"/> the mutation uses, so the
/// row is written by the same <c>SaveChanges</c> and cannot survive a rolled-back change or be
/// lost when the change succeeds.
/// </summary>
/// <remarks>
/// Metadata is restricted to identifiers, field names, and status names by construction: the
/// caller passes a small string dictionary and the values are truncated. Bodies, emails,
/// tokens, and claims are never routed through here.
/// </remarks>
internal sealed class AuditTrail(
    DanielsDojoDbContext context,
    IOperationContext operationContext,
    TimeProvider timeProvider)
{
    /// <summary>Longest metadata value retained; longer values are truncated.</summary>
    private const int MaxMetadataValueLength = 256;

    /// <summary>Longest reason retained, matching the column width.</summary>
    private const int MaxReasonLength = 512;

    private static readonly JsonSerializerOptions MetadataOptions = new()
    {
        WriteIndented = false,
    };

    /// <summary>
    /// Queues one audit row. The caller saves; nothing here calls <c>SaveChanges</c>, which is
    /// what keeps the row inside the caller's transaction.
    /// </summary>
    public void Append(
        string action,
        string targetType,
        Guid targetId,
        string? reason = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            ActorUserId = operationContext.ActorUserId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId.ToString("D"),
            Reason = Truncate(reason, MaxReasonLength),
            OccurredAtUtc = timeProvider.GetUtcNow(),
            CorrelationId = operationContext.CorrelationId,
            MetadataJson = Serialize(metadata),
        });
    }

    private static string? Serialize(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        Dictionary<string, string> bounded = metadata.ToDictionary(
            static pair => pair.Key,
            static pair => Truncate(pair.Value, MaxMetadataValueLength) ?? string.Empty,
            StringComparer.Ordinal);

        return JsonSerializer.Serialize(bounded, MetadataOptions);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
