using DanielsDojo.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Identity;

/// <summary>Maps <see cref="Role"/> to <c>identity.Roles</c>.</summary>
internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles", DatabaseSchemas.Identity);

        builder.HasKey(role => role.Id);
        builder.Property(role => role.Id).ValueGeneratedNever();

        builder.Property(role => role.Name).HasMaxLength(64).IsRequired();
        builder.Property(role => role.NormalizedName).HasMaxLength(64).IsRequired();
        builder.Property(role => role.Description).HasMaxLength(256).IsRequired();
        builder.Property(role => role.IsAssignable).IsRequired();
        builder.Property(role => role.CreatedAtUtc).AsTimestamp();
        builder.Property(role => role.UpdatedAtUtc).AsTimestamp();

        builder.HasIndex(role => role.NormalizedName)
            .IsUnique()
            .HasDatabaseName("UX_Roles_NormalizedName");
    }
}
