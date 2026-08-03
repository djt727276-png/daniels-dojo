using DanielsDojo.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Catalog;

/// <summary>Maps <see cref="Lesson"/> to <c>catalog.Lessons</c>.</summary>
internal sealed class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> builder)
    {
        builder.ToTable("Lessons", DatabaseSchemas.Catalog, table =>
        {
            table.HasCheckConstraint(
                "CK_Lessons_LessonType",
                ColumnTypes.EnumValues<LessonType>(nameof(Lesson.LessonType)));
            table.HasCheckConstraint(
                "CK_Lessons_Status",
                ColumnTypes.EnumValues<PublicationStatus>(nameof(Lesson.Status)));
            table.HasCheckConstraint(
                "CK_Lessons_SortOrder_NonNegative",
                "[SortOrder] >= 0");
            table.HasCheckConstraint(
                "CK_Lessons_EstimatedDurationSeconds_NonNegative",
                "[EstimatedDurationSeconds] IS NULL OR [EstimatedDurationSeconds] >= 0");
        });

        builder.HasKey(lesson => lesson.Id);
        builder.Property(lesson => lesson.Id).ValueGeneratedNever();

        builder.Property(lesson => lesson.Slug).HasMaxLength(128).IsRequired();
        builder.Property(lesson => lesson.Title).HasMaxLength(200).IsRequired();
        builder.Property(lesson => lesson.Summary).HasMaxLength(512);
        builder.Property(lesson => lesson.LessonType).AsEnumString();
        builder.Property(lesson => lesson.BodyMarkdown);
        builder.Property(lesson => lesson.SortOrder).IsRequired();
        builder.Property(lesson => lesson.IsPreview).IsRequired();
        builder.Property(lesson => lesson.Status).AsEnumString();
        builder.Property(lesson => lesson.CreatedAtUtc).AsTimestamp();
        builder.Property(lesson => lesson.UpdatedAtUtc).AsTimestamp();
        builder.Property(lesson => lesson.RowVersion).IsRowVersion();

        builder.HasOne(lesson => lesson.Course)
            .WithMany(course => course.Lessons)
            .HasForeignKey(lesson => lesson.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite foreign key against the section alternate key. A lesson therefore cannot
        // reference a section that belongs to a different course — the database rejects it
        // rather than relying on application code to check.
        builder.HasOne(lesson => lesson.CourseSection)
            .WithMany(section => section.Lessons)
            .HasForeignKey(lesson => new { lesson.CourseId, lesson.CourseSectionId })
            .HasPrincipalKey(section => new { section.CourseId, section.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Lessons_CourseSections_CourseId_CourseSectionId");

        builder.HasIndex(lesson => new { lesson.CourseId, lesson.Slug })
            .IsUnique()
            .HasDatabaseName("UX_Lessons_CourseId_Slug");

        builder.HasIndex(lesson => new { lesson.CourseSectionId, lesson.SortOrder })
            .IsUnique()
            .HasDatabaseName("UX_Lessons_CourseSectionId_SortOrder");

        builder.HasIndex(lesson => lesson.Status)
            .HasDatabaseName("IX_Lessons_Status");
    }
}
