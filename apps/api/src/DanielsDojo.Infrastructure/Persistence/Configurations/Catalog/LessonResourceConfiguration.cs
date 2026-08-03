using DanielsDojo.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Catalog;

/// <summary>Maps <see cref="LessonResource"/> to <c>catalog.LessonResources</c>.</summary>
internal sealed class LessonResourceConfiguration : IEntityTypeConfiguration<LessonResource>
{
    public void Configure(EntityTypeBuilder<LessonResource> builder)
    {
        builder.ToTable("LessonResources", DatabaseSchemas.Catalog, table =>
        {
            table.HasCheckConstraint(
                "CK_LessonResources_Status",
                ColumnTypes.EnumValues<PublicationStatus>(nameof(LessonResource.Status)));
            table.HasCheckConstraint(
                "CK_LessonResources_SizeBytes_NonNegative",
                "[SizeBytes] >= 0");

            // A draft resource may still be awaiting its upload; a published one may not.
            table.HasCheckConstraint(
                "CK_LessonResources_PublishedRequiresBlob",
                "[Status] <> 'Published' OR [BlobObjectName] IS NOT NULL");
        });

        builder.HasKey(resource => resource.Id);
        builder.Property(resource => resource.Id).ValueGeneratedNever();

        builder.Property(resource => resource.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(resource => resource.BlobObjectName).HasMaxLength(256);
        builder.Property(resource => resource.MediaType).HasMaxLength(128).IsRequired();
        builder.Property(resource => resource.SizeBytes).IsRequired();
        builder.Property(resource => resource.SortOrder).IsRequired();
        builder.Property(resource => resource.Status).AsEnumString();
        builder.Property(resource => resource.CreatedAtUtc).AsTimestamp();
        builder.Property(resource => resource.UpdatedAtUtc).AsTimestamp();
        builder.Property(resource => resource.RowVersion).IsRowVersion();

        builder.HasOne(resource => resource.Lesson)
            .WithMany(lesson => lesson.Resources)
            .HasForeignKey(resource => resource.LessonId)
            .OnDelete(DeleteBehavior.Restrict);

        // One blob object backs exactly one resource row; drafts without a blob are exempt.
        builder.HasIndex(resource => resource.BlobObjectName)
            .IsUnique()
            .HasFilter("[BlobObjectName] IS NOT NULL")
            .HasDatabaseName("UX_LessonResources_BlobObjectName");

        builder.HasIndex(resource => new { resource.LessonId, resource.SortOrder })
            .HasDatabaseName("IX_LessonResources_LessonId_SortOrder");
    }
}
