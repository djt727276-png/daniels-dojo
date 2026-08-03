using DanielsDojo.Domain.Catalog;
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
                "CK_LessonVideos_DurationSeconds_NonNegative",
                "[DurationSeconds] IS NULL OR [DurationSeconds] >= 0");
        });

        builder.HasKey(video => video.Id);
        builder.Property(video => video.Id).ValueGeneratedNever();

        builder.Property(video => video.MuxAssetId).HasMaxLength(128);
        builder.Property(video => video.MuxPlaybackId).HasMaxLength(128);
        builder.Property(video => video.Status).AsEnumString();
        builder.Property(video => video.FailureCode).HasMaxLength(64);
        builder.Property(video => video.CreatedAtUtc).AsTimestamp();
        builder.Property(video => video.UpdatedAtUtc).AsTimestamp();
        builder.Property(video => video.RowVersion).IsRowVersion();

        builder.HasOne(video => video.Lesson)
            .WithOne(lesson => lesson.Video)
            .HasForeignKey<LessonVideo>(video => video.LessonId)
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
    }
}
