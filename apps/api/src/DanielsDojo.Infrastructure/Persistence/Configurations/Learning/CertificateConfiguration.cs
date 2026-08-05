using DanielsDojo.Domain.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Learning;

/// <summary>Maps <see cref="Certificate"/> to <c>learning.Certificates</c>.</summary>
/// <remarks>
/// One active certificate per member per course, enforced by the database. The verification
/// code is unique across the table because it is the public lookup key.
/// </remarks>
internal sealed class CertificateConfiguration : IEntityTypeConfiguration<Certificate>
{
    public void Configure(EntityTypeBuilder<Certificate> builder)
    {
        builder.ToTable("Certificates", DatabaseSchemas.Learning, table =>
        {
            // A revocation without a reason is not accountability.
            table.HasCheckConstraint(
                "CK_Certificates_RevocationReason",
                "([RevokedAtUtc] IS NULL AND [RevocationReason] IS NULL) "
                + "OR ([RevokedAtUtc] IS NOT NULL AND [RevocationReason] IS NOT NULL)");
        });

        builder.HasKey(certificate => certificate.Id);
        builder.Property(certificate => certificate.Id).ValueGeneratedNever();

        builder.Property(certificate => certificate.VerificationCode)
            .HasMaxLength(32)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(certificate => certificate.CourseTitleAtIssue)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(certificate => certificate.HolderNameAtIssue)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(certificate => certificate.RevocationReason).HasMaxLength(512);
        builder.Property(certificate => certificate.IssuedAtUtc).AsTimestamp();
        builder.Property(certificate => certificate.RevokedAtUtc).AsTimestamp();
        builder.Property(certificate => certificate.CreatedAtUtc).AsTimestamp();
        builder.Property(certificate => certificate.UpdatedAtUtc).AsTimestamp();
        builder.Property(certificate => certificate.RowVersion).IsRowVersion();

        builder.HasOne(certificate => certificate.User)
            .WithMany()
            .HasForeignKey(certificate => certificate.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(certificate => certificate.Course)
            .WithMany()
            .HasForeignKey(certificate => certificate.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(certificate => certificate.VerificationCode)
            .IsUnique()
            .HasDatabaseName("UX_Certificates_VerificationCode");

        builder.HasIndex(certificate => new { certificate.UserId, certificate.CourseId })
            .IsUnique()
            .HasDatabaseName("UX_Certificates_UserId_CourseId");
    }
}
