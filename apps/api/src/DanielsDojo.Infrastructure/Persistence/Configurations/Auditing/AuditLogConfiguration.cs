using DanielsDojo.Domain.Auditing;
using DanielsDojo.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Auditing;

/// <summary>
/// Maps <see cref="AuditLog"/> to <c>audit.AuditLogs</c>. The table is append-only: there is
/// no rowversion because rows are never updated, and the actor reference is restrictive so
/// removing a user cannot erase the trail.
/// </summary>
internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs", DatabaseSchemas.Audit);

        builder.HasKey(log => log.Id);
        builder.Property(log => log.Id).ValueGeneratedNever();

        builder.Property(log => log.Action).HasMaxLength(128).IsRequired();
        builder.Property(log => log.TargetType).HasMaxLength(128).IsRequired();
        builder.Property(log => log.TargetId).HasMaxLength(128).IsRequired();
        builder.Property(log => log.Reason).HasMaxLength(512);
        builder.Property(log => log.OccurredAtUtc).AsTimestamp();
        builder.Property(log => log.CorrelationId).HasMaxLength(64).IsRequired();

        // Bounded so an oversized or unredacted blob cannot be written by accident.
        builder.Property(log => log.MetadataJson).HasMaxLength(4000);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(log => log.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(log => log.OccurredAtUtc)
            .HasDatabaseName("IX_AuditLogs_OccurredAtUtc");

        builder.HasIndex(log => new { log.TargetType, log.TargetId })
            .HasDatabaseName("IX_AuditLogs_TargetType_TargetId");

        builder.HasIndex(log => log.ActorUserId)
            .HasDatabaseName("IX_AuditLogs_ActorUserId");
    }
}
