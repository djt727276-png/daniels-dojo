using DanielsDojo.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Identity;

/// <summary>Maps <see cref="User"/> to <c>identity.Users</c>.</summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", DatabaseSchemas.Identity, table =>
            table.HasCheckConstraint(
                "CK_Users_Status",
                ColumnTypes.EnumValues<UserStatus>(nameof(User.Status))));

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();

        builder.Property(user => user.IdentityProvider).HasMaxLength(64).IsRequired();
        builder.Property(user => user.ExternalIssuer).HasMaxLength(256).IsRequired();
        builder.Property(user => user.ExternalSubjectId).HasMaxLength(128).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(256).IsRequired();
        builder.Property(user => user.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(user => user.DisplayName).HasMaxLength(128).IsRequired();
        builder.Property(user => user.EmailVerified).IsRequired();
        builder.Property(user => user.Status).AsEnumString();
        builder.Property(user => user.CreatedAtUtc).AsTimestamp();
        builder.Property(user => user.UpdatedAtUtc).AsTimestamp();
        builder.Property(user => user.RowVersion).IsRowVersion();

        // Account ownership is the external issuer/subject pair, never the email address.
        builder.HasIndex(user => new { user.ExternalIssuer, user.ExternalSubjectId })
            .IsUnique()
            .HasDatabaseName("UX_Users_ExternalIssuer_ExternalSubjectId");

        // Lookup only: a provider may legitimately present the same address twice.
        builder.HasIndex(user => user.NormalizedEmail)
            .HasDatabaseName("IX_Users_NormalizedEmail");
    }
}
