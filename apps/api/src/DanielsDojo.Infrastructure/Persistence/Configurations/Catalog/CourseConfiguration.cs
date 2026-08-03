using DanielsDojo.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Catalog;

/// <summary>Maps <see cref="Course"/> to <c>catalog.Courses</c>.</summary>
internal sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable("Courses", DatabaseSchemas.Catalog, table =>
        {
            table.HasCheckConstraint(
                "CK_Courses_Level",
                ColumnTypes.EnumValues<CourseLevel>(nameof(Course.Level)));
            table.HasCheckConstraint(
                "CK_Courses_Status",
                ColumnTypes.EnumValues<PublicationStatus>(nameof(Course.Status)));
        });

        builder.HasKey(course => course.Id);
        builder.Property(course => course.Id).ValueGeneratedNever();

        builder.Property(course => course.Slug).HasMaxLength(128).IsRequired();
        builder.Property(course => course.Title).HasMaxLength(200).IsRequired();
        builder.Property(course => course.Summary).HasMaxLength(512).IsRequired();
        builder.Property(course => course.Description).HasMaxLength(4000).IsRequired();
        builder.Property(course => course.ImageStorageKey).HasMaxLength(256);
        builder.Property(course => course.ImageAltText).HasMaxLength(256);
        builder.Property(course => course.Level).AsEnumString();
        builder.Property(course => course.Status).AsEnumString();
        builder.Property(course => course.IncludedInMembership).IsRequired();
        builder.Property(course => course.PublishedAtUtc).AsTimestamp();
        builder.Property(course => course.CreatedAtUtc).AsTimestamp();
        builder.Property(course => course.UpdatedAtUtc).AsTimestamp();
        builder.Property(course => course.RowVersion).IsRowVersion();

        builder.HasIndex(course => course.Slug)
            .IsUnique()
            .HasDatabaseName("UX_Courses_Slug");

        builder.HasIndex(course => course.Status)
            .HasDatabaseName("IX_Courses_Status");

        builder.HasIndex(course => course.IncludedInMembership)
            .HasDatabaseName("IX_Courses_IncludedInMembership");
    }
}
