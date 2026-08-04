using DanielsDojo.Domain.Community;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Community;

/// <summary>Maps <see cref="CommunityProfile"/> to <c>community.Profiles</c>.</summary>
internal sealed class CommunityProfileConfiguration : IEntityTypeConfiguration<CommunityProfile>
{
    public void Configure(EntityTypeBuilder<CommunityProfile> builder)
    {
        builder.ToTable("Profiles", DatabaseSchemas.Community, table =>
        {
            table.HasCheckConstraint(
                "CK_Profiles_FriendRequestPolicy",
                ColumnTypes.EnumValues<FriendRequestPolicy>(nameof(CommunityProfile.FriendRequestPolicy)));

            // MessagePolicy has no "Everyone" member, so this constraint is what stops a
            // future value being written before the product decides to offer it.
            table.HasCheckConstraint(
                "CK_Profiles_MessagePolicy",
                ColumnTypes.EnumValues<MessagePolicy>(nameof(CommunityProfile.MessagePolicy)));

            table.HasCheckConstraint(
                "CK_Profiles_Status",
                ColumnTypes.EnumValues<CommunityProfileStatus>(nameof(CommunityProfile.Status)));

            // Guidelines version and acceptance time are recorded together or not at all.
            table.HasCheckConstraint(
                "CK_Profiles_GuidelinesPaired",
                "([GuidelinesVersion] IS NULL AND [GuidelinesAcceptedAtUtc] IS NULL) " +
                "OR ([GuidelinesVersion] IS NOT NULL AND [GuidelinesAcceptedAtUtc] IS NOT NULL)");
        });

        builder.HasKey(profile => profile.UserId);
        builder.Property(profile => profile.UserId).ValueGeneratedNever();

        builder.Property(profile => profile.Handle).HasMaxLength(32).IsRequired();
        builder.Property(profile => profile.NormalizedHandle).HasMaxLength(32).IsRequired();
        builder.Property(profile => profile.Bio).HasMaxLength(500);
        builder.Property(profile => profile.AvatarStorageKey).HasMaxLength(256);
        builder.Property(profile => profile.IsDiscoverable).IsRequired();
        builder.Property(profile => profile.FriendRequestPolicy).AsEnumString();
        builder.Property(profile => profile.MessagePolicy).AsEnumString();
        builder.Property(profile => profile.Status).AsEnumString();
        builder.Property(profile => profile.GuidelinesVersion).HasMaxLength(32);
        builder.Property(profile => profile.GuidelinesAcceptedAtUtc).AsTimestamp();
        builder.Property(profile => profile.EligibilityAttestedAtUtc).AsTimestamp();
        builder.Property(profile => profile.CreatedAtUtc).AsTimestamp();
        builder.Property(profile => profile.UpdatedAtUtc).AsTimestamp();
        builder.Property(profile => profile.RowVersion).IsRowVersion();

        builder.HasOne(profile => profile.User)
            .WithOne()
            .HasForeignKey<CommunityProfile>(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(profile => profile.NormalizedHandle)
            .IsUnique()
            .HasDatabaseName("UX_Profiles_NormalizedHandle");

        // Search only ever reads discoverable, active profiles, so the index leads with the
        // discoverability flag.
        builder.HasIndex(profile => new { profile.IsDiscoverable, profile.NormalizedHandle })
            .HasDatabaseName("IX_Profiles_IsDiscoverable_NormalizedHandle");
    }
}
