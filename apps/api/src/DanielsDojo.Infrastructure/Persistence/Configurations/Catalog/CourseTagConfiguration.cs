using DanielsDojo.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Catalog;

/// <summary>
/// Maps <see cref="CourseTag"/> to <c>catalog.CourseTags</c>. Tag assignment is presentational
/// metadata rather than history, so cascade is appropriate here.
/// </summary>
internal sealed class CourseTagConfiguration : IEntityTypeConfiguration<CourseTag>
{
    public void Configure(EntityTypeBuilder<CourseTag> builder)
    {
        builder.ToTable("CourseTags", DatabaseSchemas.Catalog);

        builder.HasKey(courseTag => new { courseTag.CourseId, courseTag.TagId });

        builder.HasOne(courseTag => courseTag.Course)
            .WithMany(course => course.CourseTags)
            .HasForeignKey(courseTag => courseTag.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(courseTag => courseTag.Tag)
            .WithMany(tag => tag.CourseTags)
            .HasForeignKey(courseTag => courseTag.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(courseTag => courseTag.TagId)
            .HasDatabaseName("IX_CourseTags_TagId");
    }
}
