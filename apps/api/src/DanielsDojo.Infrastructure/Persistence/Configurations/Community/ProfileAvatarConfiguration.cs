using DanielsDojo.Domain.Community;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Community;

/// <summary>Maps <see cref="ProfileAvatar"/> to <c>community.ProfileAvatars</c>.</summary>
internal sealed class ProfileAvatarConfiguration : IEntityTypeConfiguration<ProfileAvatar>
{
    public void Configure(EntityTypeBuilder<ProfileAvatar> builder)
    {
        builder.ToTable("ProfileAvatars", DatabaseSchemas.Community, table =>
        {
            // Safety net under the service's own limit: even a bug in the re-encoder cannot
            // land a multi-megabyte blob in this table.
            table.HasCheckConstraint(
                "CK_ProfileAvatars_Bytes_Size",
                "DATALENGTH([Bytes]) BETWEEN 1 AND 262144");
        });

        builder.HasKey(avatar => avatar.UserId);
        builder.Property(avatar => avatar.UserId).ValueGeneratedNever();

        builder.Property(avatar => avatar.ContentType).HasMaxLength(64).IsRequired();
        builder.Property(avatar => avatar.Bytes).IsRequired();
        builder.Property(avatar => avatar.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(avatar => avatar.CreatedAtUtc).AsTimestamp();
        builder.Property(avatar => avatar.UpdatedAtUtc).AsTimestamp();

        builder.HasOne(avatar => avatar.User)
            .WithOne()
            .HasForeignKey<ProfileAvatar>(avatar => avatar.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
