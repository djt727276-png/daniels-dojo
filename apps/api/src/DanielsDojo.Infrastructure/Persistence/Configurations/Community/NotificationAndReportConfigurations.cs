using DanielsDojo.Domain.Community;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Community;

/// <summary>Maps <see cref="Notification"/> to <c>community.Notifications</c>.</summary>
internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications", DatabaseSchemas.Community, table =>
        {
            table.HasCheckConstraint(
                "CK_Notifications_Kind",
                ColumnTypes.EnumValues<NotificationKind>(nameof(Notification.Kind)));

            table.HasCheckConstraint(
                "CK_Notifications_NoSelfNotification",
                "[ActorUserId] IS NULL OR [ActorUserId] <> [RecipientUserId]");
        });

        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Id).ValueGeneratedNever();

        builder.Property(notification => notification.Kind).AsEnumString();
        builder.Property(notification => notification.TargetType).HasMaxLength(32).IsRequired();
        builder.Property(notification => notification.CreatedAtUtc).AsTimestamp();
        builder.Property(notification => notification.ReadAtUtc).AsTimestamp();

        // There is deliberately no body column: a notification points at a target, and the
        // client re-fetches it through endpoints that re-apply every privacy rule.

        builder.HasOne(notification => notification.Recipient)
            .WithMany()
            .HasForeignKey(notification => notification.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(notification => notification.Actor)
            .WithMany()
            .HasForeignKey(notification => notification.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(notification => new { notification.RecipientUserId, notification.CreatedAtUtc })
            .HasDatabaseName("IX_Notifications_RecipientUserId_CreatedAtUtc");

        // Unread counts are a hot path, so they get a filtered index of their own.
        builder.HasIndex(notification => notification.RecipientUserId)
            .HasFilter("[ReadAtUtc] IS NULL")
            .HasDatabaseName("IX_Notifications_RecipientUserId_Unread");
    }
}

/// <summary>Maps <see cref="Report"/> to <c>community.Reports</c>.</summary>
internal sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("Reports", DatabaseSchemas.Community, table =>
        {
            table.HasCheckConstraint(
                "CK_Reports_TargetType",
                ColumnTypes.EnumValues<ReportTargetType>(nameof(Report.TargetType)));
            table.HasCheckConstraint(
                "CK_Reports_ReasonCode",
                ColumnTypes.EnumValues<ReportReasonCode>(nameof(Report.ReasonCode)));
            table.HasCheckConstraint(
                "CK_Reports_Status",
                ColumnTypes.EnumValues<ReportStatus>(nameof(Report.Status)));

            table.HasCheckConstraint(
                "CK_Reports_HandledWhenClosed",
                "[Status] IN ('Open', 'Reviewing') " +
                "OR ([HandledByUserId] IS NOT NULL AND [HandledAtUtc] IS NOT NULL)");
        });

        builder.HasKey(report => report.Id);
        builder.Property(report => report.Id).ValueGeneratedNever();

        builder.Property(report => report.TargetType).AsEnumString();
        builder.Property(report => report.ReasonCode).AsEnumString();
        builder.Property(report => report.Detail).HasMaxLength(1000);
        builder.Property(report => report.Status).AsEnumString();
        builder.Property(report => report.Resolution).HasMaxLength(1000);
        builder.Property(report => report.HandledAtUtc).AsTimestamp();
        builder.Property(report => report.CreatedAtUtc).AsTimestamp();
        builder.Property(report => report.UpdatedAtUtc).AsTimestamp();
        builder.Property(report => report.RowVersion).IsRowVersion();

        builder.HasOne(report => report.Reporter)
            .WithMany()
            .HasForeignKey(report => report.ReporterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(report => report.HandledByUser)
            .WithMany()
            .HasForeignKey(report => report.HandledByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // One open report per reporter per target: a double submission cannot flood the queue,
        // while resolved history is still retained.
        builder.HasIndex(report => new { report.ReporterUserId, report.TargetType, report.TargetId })
            .IsUnique()
            .HasFilter("[Status] IN ('Open', 'Reviewing')")
            .HasDatabaseName("UX_Reports_Reporter_Target_Open");

        builder.HasIndex(report => new { report.Status, report.CreatedAtUtc })
            .HasDatabaseName("IX_Reports_Status_CreatedAtUtc");

        builder.HasIndex(report => new { report.TargetType, report.TargetId })
            .HasDatabaseName("IX_Reports_TargetType_TargetId");
    }
}
