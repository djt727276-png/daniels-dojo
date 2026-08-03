using DanielsDojo.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Catalog;

/// <summary>Maps <see cref="Tag"/> to <c>catalog.Tags</c>.</summary>
internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags", DatabaseSchemas.Catalog);

        builder.HasKey(tag => tag.Id);
        builder.Property(tag => tag.Id).ValueGeneratedNever();

        builder.Property(tag => tag.Name).HasMaxLength(64).IsRequired();
        builder.Property(tag => tag.NormalizedName).HasMaxLength(64).IsRequired();
        builder.Property(tag => tag.CreatedAtUtc).AsTimestamp();
        builder.Property(tag => tag.UpdatedAtUtc).AsTimestamp();

        builder.HasIndex(tag => tag.NormalizedName)
            .IsUnique()
            .HasDatabaseName("UX_Tags_NormalizedName");
    }
}
