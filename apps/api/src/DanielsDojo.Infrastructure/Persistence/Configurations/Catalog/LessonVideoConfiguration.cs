using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Domain.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Catalog;

/// <summary>Maps <see cref="LessonVideo"/> to <c>catalog.LessonVideos</c>.</summary>
internal sealed class LessonVideoConfiguration : IEntityTypeConfiguration<LessonVideo>
{
    public void Configure(EntityTypeBuilder<LessonVideo> builder)
    {
        builder.ToTable("LessonVideos", DatabaseSchemas.Catalog, table =>
        {
            table.HasCheckConstraint(
                "CK_LessonVideos_Status",
                ColumnTypes.EnumValues<LessonVideoStatus>(nameof(LessonVideo.Status)));
            table.HasCheckConstraint(
                "CK_LessonVideos_ProviderMode",
                ColumnTypes.EnumValues<ProviderMode>(nameof(LessonVideo.ProviderMode)));
            table.HasCheckConstraint(
                "CK_LessonVideos_DurationSeconds_NonNegative",
                "[DurationSeconds] IS NULL OR [DurationSeconds] >= 0");

            // Ready means playable, and playable means there is something to play.
            table.HasCheckConstraint(
                "CK_LessonVideos_ReadyRequiresPlayback",
                "[Status] <> 'Ready' OR [MuxPlaybackId] IS NOT NULL");

            // A replacement is only meaningful when a previous asset can still be served.
            table.HasCheckConstraint(
                "CK_LessonVideos_ReplacingRequiresLastKnownGood",
                "[Status] <> 'Replacing' OR [LastKnownGoodPlaybackId] IS NOT NULL");

            // A failure carries its reason.
            table.HasCheckConstraint(
                "CK_LessonVideos_FailureCode",
                "[Status] <> 'Failed' OR [FailureCode] IS NOT NULL");

            // A human spot check records who performed it.
            table.HasCheckConstraint(
                "CK_LessonVideos_SpotCheckActor",
                "([HumanSpotCheckAtUtc] IS NULL AND [HumanSpotCheckByUserId] IS NULL) "
                + "OR ([HumanSpotCheckAtUtc] IS NOT NULL AND [HumanSpotCheckByUserId] IS NOT NULL)");
        });

        builder.HasKey(video => video.Id);
        builder.Property(video => video.Id).ValueGeneratedNever();

        builder.Property(video => video.MuxUploadId).HasMaxLength(128).IsUnicode(false);
        builder.Property(video => video.MuxAssetId).HasMaxLength(128).IsUnicode(false);
        builder.Property(video => video.MuxPlaybackId).HasMaxLength(128).IsUnicode(false);
        builder.Property(video => video.LastKnownGoodAssetId).HasMaxLength(128).IsUnicode(false);
        builder.Property(video => video.LastKnownGoodPlaybackId).HasMaxLength(128).IsUnicode(false);
        builder.Property(video => video.IsSignedPlayback).IsRequired();
        builder.Property(video => video.Status).AsEnumString();
        builder.Property(video => video.ProviderMode).AsEnumString();
        builder.Property(video => video.AspectRatio).HasMaxLength(16).IsUnicode(false);
        builder.Property(video => video.FailureCode).HasMaxLength(64).IsUnicode(false);
        builder.Property(video => video.LastProviderEventAtUtc).AsTimestamp();
        builder.Property(video => video.AdminPlaybackVerifiedAtUtc).AsTimestamp();
        builder.Property(video => video.StudentPlaybackVerifiedAtUtc).AsTimestamp();
        builder.Property(video => video.HumanSpotCheckAtUtc).AsTimestamp();
        builder.Property(video => video.CreatedAtUtc).AsTimestamp();
        builder.Property(video => video.UpdatedAtUtc).AsTimestamp();
        builder.Property(video => video.RowVersion).IsRowVersion();

        builder.HasOne(video => video.Lesson)
            .WithOne(lesson => lesson.Video)
            .HasForeignKey<LessonVideo>(video => video.LessonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(video => video.CurrentSource)
            .WithMany()
            .HasForeignKey(video => video.CurrentSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MediaSource>()
            .WithMany()
            .HasForeignKey(video => video.IncomingSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(video => video.HumanSpotCheckByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(video => video.LessonId)
            .IsUnique()
            .HasDatabaseName("UX_LessonVideos_LessonId");

        // Filtered: many lessons legitimately have no provider asset yet.
        builder.HasIndex(video => video.MuxAssetId)
            .IsUnique()
            .HasFilter("[MuxAssetId] IS NOT NULL")
            .HasDatabaseName("UX_LessonVideos_MuxAssetId");

        builder.HasIndex(video => video.MuxPlaybackId)
            .IsUnique()
            .HasFilter("[MuxPlaybackId] IS NOT NULL")
            .HasDatabaseName("UX_LessonVideos_MuxPlaybackId");

        builder.HasIndex(video => video.MuxUploadId)
            .IsUnique()
            .HasFilter("[MuxUploadId] IS NOT NULL")
            .HasDatabaseName("UX_LessonVideos_MuxUploadId");

        builder.HasIndex(video => video.Status)
            .HasDatabaseName("IX_LessonVideos_Status");
    }
}
