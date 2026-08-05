using DanielsDojo.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Platform;

/// <summary>Maps <see cref="FeatureFlag"/> to <c>platform.FeatureFlags</c>.</summary>
internal sealed class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.ToTable("FeatureFlags", DatabaseSchemas.Platform);

        builder.HasKey(flag => flag.Key);
        builder.Property(flag => flag.Key).HasMaxLength(64).ValueGeneratedNever();

        builder.Property(flag => flag.Enabled).IsRequired();
        builder.Property(flag => flag.Description).HasMaxLength(200).IsRequired();
        builder.Property(flag => flag.CreatedAtUtc).AsTimestamp();
        builder.Property(flag => flag.UpdatedAtUtc).AsTimestamp();
    }
}
