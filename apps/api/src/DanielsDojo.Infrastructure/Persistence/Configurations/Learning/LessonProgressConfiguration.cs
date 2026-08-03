using DanielsDojo.Domain.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Learning;

/// <summary>Maps <see cref="LessonProgress"/> to <c>learning.LessonProgress</c>.</summary>
internal sealed class LessonProgressConfiguration : IEntityTypeConfiguration<LessonProgress>
{
    public void Configure(EntityTypeBuilder<LessonProgress> builder)
    {
        builder.ToTable("LessonProgress", DatabaseSchemas.Learning, table =>
        {
            table.HasCheckConstraint(
                "CK_LessonProgress_LastPositionSeconds_NonNegative",
                "[LastPositionSeconds] >= 0");

            // Completion is represented by its timestamp, and a lesson cannot be completed
            // without also having been started.
            table.HasCheckConstraint(
                "CK_LessonProgress_CompletedRequiresStarted",
                "[CompletedAtUtc] IS NULL OR [StartedAtUtc] IS NOT NULL");
            table.HasCheckConstraint(
                "CK_LessonProgress_CompletedAfterStarted",
                "[CompletedAtUtc] IS NULL OR [CompletedAtUtc] >= [StartedAtUtc]");
        });

        builder.HasKey(progress => progress.Id);
        builder.Property(progress => progress.Id).ValueGeneratedNever();

        builder.Property(progress => progress.StartedAtUtc).AsTimestamp();
        builder.Property(progress => progress.CompletedAtUtc).AsTimestamp();
        builder.Property(progress => progress.LastPositionSeconds).IsRequired();
        builder.Property(progress => progress.CreatedAtUtc).AsTimestamp();
        builder.Property(progress => progress.UpdatedAtUtc).AsTimestamp();
        builder.Property(progress => progress.RowVersion).IsRowVersion();

        builder.HasOne(progress => progress.User)
            .WithMany()
            .HasForeignKey(progress => progress.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(progress => progress.Lesson)
            .WithMany()
            .HasForeignKey(progress => progress.LessonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(progress => new { progress.UserId, progress.LessonId })
            .IsUnique()
            .HasDatabaseName("UX_LessonProgress_UserId_LessonId");
    }
}
