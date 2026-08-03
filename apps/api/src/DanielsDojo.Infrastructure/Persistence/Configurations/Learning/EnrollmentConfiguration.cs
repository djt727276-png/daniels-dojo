using DanielsDojo.Domain.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Learning;

/// <summary>Maps <see cref="Enrollment"/> to <c>learning.Enrollments</c>.</summary>
internal sealed class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.ToTable("Enrollments", DatabaseSchemas.Learning);

        builder.HasKey(enrollment => enrollment.Id);
        builder.Property(enrollment => enrollment.Id).ValueGeneratedNever();

        builder.Property(enrollment => enrollment.EnrolledAtUtc).AsTimestamp();
        builder.Property(enrollment => enrollment.LastAccessedAtUtc).AsTimestamp();
        builder.Property(enrollment => enrollment.CreatedAtUtc).AsTimestamp();
        builder.Property(enrollment => enrollment.UpdatedAtUtc).AsTimestamp();

        builder.HasOne(enrollment => enrollment.User)
            .WithMany()
            .HasForeignKey(enrollment => enrollment.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(enrollment => enrollment.Course)
            .WithMany()
            .HasForeignKey(enrollment => enrollment.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(enrollment => new { enrollment.UserId, enrollment.CourseId })
            .IsUnique()
            .HasDatabaseName("UX_Enrollments_UserId_CourseId");
    }
}
