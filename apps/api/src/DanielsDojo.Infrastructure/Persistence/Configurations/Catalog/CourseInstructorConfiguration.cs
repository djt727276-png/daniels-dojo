using DanielsDojo.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Catalog;

/// <summary>
/// Maps <see cref="CourseInstructor"/> to <c>catalog.CourseInstructors</c>. The user side is
/// restrictive because it points at a person; the course side is attribution metadata.
/// </summary>
internal sealed class CourseInstructorConfiguration : IEntityTypeConfiguration<CourseInstructor>
{
    public void Configure(EntityTypeBuilder<CourseInstructor> builder)
    {
        builder.ToTable("CourseInstructors", DatabaseSchemas.Catalog);

        builder.HasKey(instructor => new { instructor.CourseId, instructor.UserId });

        builder.Property(instructor => instructor.AssignedAtUtc).AsTimestamp();

        builder.HasOne(instructor => instructor.Course)
            .WithMany(course => course.Instructors)
            .HasForeignKey(instructor => instructor.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(instructor => instructor.User)
            .WithMany()
            .HasForeignKey(instructor => instructor.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(instructor => instructor.UserId)
            .HasDatabaseName("IX_CourseInstructors_UserId");
    }
}
