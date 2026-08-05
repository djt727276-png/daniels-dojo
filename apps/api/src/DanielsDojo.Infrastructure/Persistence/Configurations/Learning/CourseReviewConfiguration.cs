using DanielsDojo.Domain.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Learning;

/// <summary>Maps <see cref="CourseReview"/> to <c>learning.CourseReviews</c>.</summary>
internal sealed class CourseReviewConfiguration : IEntityTypeConfiguration<CourseReview>
{
    public void Configure(EntityTypeBuilder<CourseReview> builder)
    {
        builder.ToTable("CourseReviews", DatabaseSchemas.Learning, table =>
        {
            table.HasCheckConstraint("CK_CourseReviews_Rating", "[Rating] BETWEEN 1 AND 5");
            table.HasCheckConstraint(
                "CK_CourseReviews_Status",
                ColumnTypes.EnumValues<CourseReviewStatus>(nameof(CourseReview.Status)));

            // A hidden review must say why; anything else must not carry a stale reason.
            table.HasCheckConstraint(
                "CK_CourseReviews_ModerationReason",
                "([Status] = 'Hidden' AND [ModerationReason] IS NOT NULL) "
                + "OR ([Status] <> 'Hidden' AND [ModerationReason] IS NULL)");
        });

        builder.HasKey(review => review.Id);
        builder.Property(review => review.Id).ValueGeneratedNever();

        builder.Property(review => review.Status).AsEnumString();
        builder.Property(review => review.Body).HasMaxLength(4000).IsRequired();
        builder.Property(review => review.ModerationReason).HasMaxLength(512);
        builder.Property(review => review.EditedAtUtc).AsTimestamp();
        builder.Property(review => review.CreatedAtUtc).AsTimestamp();
        builder.Property(review => review.UpdatedAtUtc).AsTimestamp();
        builder.Property(review => review.RowVersion).IsRowVersion();

        builder.HasOne(review => review.User)
            .WithMany()
            .HasForeignKey(review => review.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(review => review.Course)
            .WithMany()
            .HasForeignKey(review => review.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        // One review per member per course, ever — an author edits or deletes their own
        // rather than stacking new ones.
        builder.HasIndex(review => new { review.UserId, review.CourseId })
            .IsUnique()
            .HasDatabaseName("UX_CourseReviews_UserId_CourseId");

        builder.HasIndex(review => new { review.CourseId, review.Status })
            .HasDatabaseName("IX_CourseReviews_CourseId_Status");
    }
}
