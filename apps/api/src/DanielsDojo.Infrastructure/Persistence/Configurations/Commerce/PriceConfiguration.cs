using DanielsDojo.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Commerce;

/// <summary>Maps <see cref="Price"/> to <c>commerce.Prices</c>.</summary>
internal sealed class PriceConfiguration : IEntityTypeConfiguration<Price>
{
    public void Configure(EntityTypeBuilder<Price> builder)
    {
        builder.ToTable("Prices", DatabaseSchemas.Commerce, table =>
        {
            table.HasCheckConstraint(
                "CK_Prices_BillingInterval",
                ColumnTypes.EnumValues<BillingInterval>(nameof(Price.BillingInterval)));
            table.HasCheckConstraint(
                "CK_Prices_Status",
                ColumnTypes.EnumValues<CommerceStatus>(nameof(Price.Status)));
            table.HasCheckConstraint(
                "CK_Prices_AmountMinor_Positive",
                "[AmountMinor] > 0");
            table.HasCheckConstraint(
                "CK_Prices_Currency_Uppercase",
                ColumnTypes.UppercaseCurrency(nameof(Price.Currency)));

            // Launch sells only single-interval cycles; multi-interval billing is future work.
            table.HasCheckConstraint(
                "CK_Prices_BillingIntervalCount_One",
                "[BillingIntervalCount] = 1");
            table.HasCheckConstraint(
                "CK_Prices_RetiredAfterEffective",
                "[RetiredAtUtc] IS NULL OR [RetiredAtUtc] >= [EffectiveFromUtc]");
        });

        builder.HasKey(price => price.Id);
        builder.Property(price => price.Id).ValueGeneratedNever();

        builder.Property(price => price.AmountMinor).IsRequired();
        builder.Property(price => price.Currency).AsCurrency();
        builder.Property(price => price.BillingInterval).AsEnumString();
        builder.Property(price => price.BillingIntervalCount).IsRequired();
        builder.Property(price => price.StripePriceId).HasMaxLength(128);
        builder.Property(price => price.Status).AsEnumString();
        builder.Property(price => price.EffectiveFromUtc).AsTimestamp();
        builder.Property(price => price.RetiredAtUtc).AsTimestamp();
        builder.Property(price => price.CreatedAtUtc).AsTimestamp();
        builder.Property(price => price.UpdatedAtUtc).AsTimestamp();
        builder.Property(price => price.RowVersion).IsRowVersion();

        builder.HasOne(price => price.Offer)
            .WithMany(offer => offer.Prices)
            .HasForeignKey(price => price.OfferId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(price => price.StripePriceId)
            .IsUnique()
            .HasFilter("[StripePriceId] IS NOT NULL")
            .HasDatabaseName("UX_Prices_StripePriceId");

        builder.HasIndex(price => new { price.OfferId, price.Status })
            .HasDatabaseName("IX_Prices_OfferId_Status");
    }
}
