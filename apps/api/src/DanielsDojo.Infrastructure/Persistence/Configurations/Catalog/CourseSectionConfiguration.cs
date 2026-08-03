using DanielsDojo.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Catalog;

/// <summary>Maps <see cref="CourseSection"/> to <c>catalog.CourseSections</c>.</summary>
internal sealed class CourseSectionConfiguration : IEntityTypeConfiguration<CourseSection>
{
    public void Configure(EntityTypeBuilder<CourseSection> builder)
    {
        builder.ToTable("CourseSections", DatabaseSchemas.Catalog, table =>
            table.HasCheckConstraint(
                "CK_CourseSections_Status",
                ColumnTypes.EnumValues<PublicationStatus>(nameof(CourseSection.Status))));

        builder.HasKey(section => section.Id);
        builder.Property(section => section.Id).ValueGeneratedNever();

        builder.Property(section => section.Title).HasMaxLength(200).IsRequired();
        builder.Property(section => section.Description).HasMaxLength(1000);
        builder.Property(section => section.SortOrder).IsRequired();
        builder.Property(section => section.Status).AsEnumString();
        builder.Property(section => section.CreatedAtUtc).AsTimestamp();
        builder.Property(section => section.UpdatedAtUtc).AsTimestamp();
        builder.Property(section => section.RowVersion).IsRowVersion();

        // Alternate key targeted by the Lesson composite foreign key. It is what proves a
        // lesson's section belongs to the lesson's course.
        builder.HasAlternateKey(section => new { section.CourseId, section.Id })
            .HasName("AK_CourseSections_CourseId_Id");

        builder.HasOne(section => section.Course)
            .WithMany(course => course.Sections)
            .HasForeignKey(section => section.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(section => new { section.CourseId, section.SortOrder })
            .IsUnique()
            .HasDatabaseName("UX_CourseSections_CourseId_SortOrder");
    }
}
