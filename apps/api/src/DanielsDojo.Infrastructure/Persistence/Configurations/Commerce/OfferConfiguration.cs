using DanielsDojo.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Commerce;

/// <summary>Maps <see cref="Offer"/> to <c>commerce.Offers</c>.</summary>
internal sealed class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.ToTable("Offers", DatabaseSchemas.Commerce, table =>
        {
            table.HasCheckConstraint(
                "CK_Offers_Kind",
                ColumnTypes.EnumValues<OfferKind>(nameof(Offer.Kind)));
            table.HasCheckConstraint(
                "CK_Offers_Status",
                ColumnTypes.EnumValues<CommerceStatus>(nameof(Offer.Status)));

            // A lifetime offer must name the course it sells...
            table.HasCheckConstraint(
                "CK_Offers_CourseLifetimeRequiresCourse",
                "[Kind] <> 'CourseLifetime' OR [CourseId] IS NOT NULL");

            // ...and a membership offer must not, because it covers many courses.
            table.HasCheckConstraint(
                "CK_Offers_MembershipForbidsCourse",
                "[Kind] <> 'Membership' OR [CourseId] IS NULL");
        });

        builder.HasKey(offer => offer.Id);
        builder.Property(offer => offer.Id).ValueGeneratedNever();

        builder.Property(offer => offer.Code).HasMaxLength(64).IsRequired();
        builder.Property(offer => offer.Name).HasMaxLength(200).IsRequired();
        builder.Property(offer => offer.Description).HasMaxLength(1000).IsRequired();
        builder.Property(offer => offer.Kind).AsEnumString();
        builder.Property(offer => offer.StripeProductId).HasMaxLength(128);
        builder.Property(offer => offer.Status).AsEnumString();
        builder.Property(offer => offer.CreatedAtUtc).AsTimestamp();
        builder.Property(offer => offer.UpdatedAtUtc).AsTimestamp();
        builder.Property(offer => offer.RowVersion).IsRowVersion();

        builder.HasOne(offer => offer.Course)
            .WithMany()
            .HasForeignKey(offer => offer.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(offer => offer.Code)
            .IsUnique()
            .HasDatabaseName("UX_Offers_Code");

        // Filtered: offers exist before they are created at the payment provider.
        builder.HasIndex(offer => offer.StripeProductId)
            .IsUnique()
            .HasFilter("[StripeProductId] IS NOT NULL")
            .HasDatabaseName("UX_Offers_StripeProductId");

        builder.HasIndex(offer => offer.Status)
            .HasDatabaseName("IX_Offers_Status");
    }
}
