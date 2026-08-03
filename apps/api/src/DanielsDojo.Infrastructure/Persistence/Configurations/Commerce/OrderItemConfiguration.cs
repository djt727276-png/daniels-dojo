using DanielsDojo.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Commerce;

/// <summary>Maps <see cref="OrderItem"/> to <c>commerce.OrderItems</c>.</summary>
internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems", DatabaseSchemas.Commerce, table =>
        {
            // Launch sells one seat per line; quantity exists for future bundles.
            table.HasCheckConstraint(
                "CK_OrderItems_Quantity_One",
                "[Quantity] = 1");
            table.HasCheckConstraint(
                "CK_OrderItems_UnitAmountMinor_NonNegative",
                "[UnitAmountMinor] >= 0");
            table.HasCheckConstraint(
                "CK_OrderItems_LineTotal_Reconciles",
                "[LineTotalMinor] = [UnitAmountMinor] * [Quantity]");
            table.HasCheckConstraint(
                "CK_OrderItems_Currency_Uppercase",
                ColumnTypes.UppercaseCurrency(nameof(OrderItem.Currency)));
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();

        builder.Property(item => item.OfferName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.UnitAmountMinor).IsRequired();
        builder.Property(item => item.Currency).AsCurrency();
        builder.Property(item => item.Quantity).IsRequired();
        builder.Property(item => item.LineTotalMinor).IsRequired();

        builder.HasOne(item => item.Order)
            .WithMany(order => order.Items)
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(item => item.Offer)
            .WithMany()
            .HasForeignKey(item => item.OfferId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(item => item.Price)
            .WithMany()
            .HasForeignKey(item => item.PriceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(item => item.Course)
            .WithMany()
            .HasForeignKey(item => item.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(item => new { item.OrderId, item.OfferId })
            .IsUnique()
            .HasDatabaseName("UX_OrderItems_OrderId_OfferId");

        builder.HasIndex(item => item.CourseId)
            .HasDatabaseName("IX_OrderItems_CourseId");
    }
}
