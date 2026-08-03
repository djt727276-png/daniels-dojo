using DanielsDojo.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Commerce;

/// <summary>
/// Maps <see cref="WebhookEvent"/> to <c>commerce.WebhookEvents</c>. The unique
/// (Provider, ExternalEventId) index is the idempotency guarantee for redelivered events.
/// </summary>
internal sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable("WebhookEvents", DatabaseSchemas.Commerce, table =>
        {
            table.HasCheckConstraint(
                "CK_WebhookEvents_Status",
                ColumnTypes.EnumValues<WebhookEventStatus>(nameof(WebhookEvent.Status)));
            table.HasCheckConstraint(
                "CK_WebhookEvents_AttemptCount_NonNegative",
                "[AttemptCount] >= 0");
        });

        builder.HasKey(webhookEvent => webhookEvent.Id);
        builder.Property(webhookEvent => webhookEvent.Id).ValueGeneratedNever();

        builder.Property(webhookEvent => webhookEvent.Provider).HasMaxLength(32).IsRequired();
        builder.Property(webhookEvent => webhookEvent.ExternalEventId).HasMaxLength(128).IsRequired();
        builder.Property(webhookEvent => webhookEvent.EventType).HasMaxLength(128).IsRequired();
        builder.Property(webhookEvent => webhookEvent.Status).AsEnumString();
        builder.Property(webhookEvent => webhookEvent.AttemptCount).IsRequired();
        builder.Property(webhookEvent => webhookEvent.ReceivedAtUtc).AsTimestamp();
        builder.Property(webhookEvent => webhookEvent.ProcessedAtUtc).AsTimestamp();
        builder.Property(webhookEvent => webhookEvent.NextAttemptAtUtc).AsTimestamp();

        // Bounded and redacted by the caller: never a raw payload or provider secret.
        builder.Property(webhookEvent => webhookEvent.LastError).HasMaxLength(1024);

        // Digest only — the complete payload is deliberately not persisted.
        builder.Property(webhookEvent => webhookEvent.PayloadSha256)
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsFixedLength()
            .IsRequired();

        builder.HasIndex(webhookEvent => new { webhookEvent.Provider, webhookEvent.ExternalEventId })
            .IsUnique()
            .HasDatabaseName("UX_WebhookEvents_Provider_ExternalEventId");

        builder.HasIndex(webhookEvent => new { webhookEvent.Status, webhookEvent.NextAttemptAtUtc })
            .HasDatabaseName("IX_WebhookEvents_Status_NextAttemptAtUtc");
    }
}
