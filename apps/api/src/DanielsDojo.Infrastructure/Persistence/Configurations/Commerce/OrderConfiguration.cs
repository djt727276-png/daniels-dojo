using DanielsDojo.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Commerce;

/// <summary>Maps <see cref="Order"/> to <c>commerce.Orders</c>.</summary>
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", DatabaseSchemas.Commerce, table =>
        {
            table.HasCheckConstraint(
                "CK_Orders_Status",
                ColumnTypes.EnumValues<OrderStatus>(nameof(Order.Status)));
            table.HasCheckConstraint(
                "CK_Orders_Amounts_NonNegative",
                "[SubtotalMinor] >= 0 AND [TaxMinor] >= 0 AND [TotalMinor] >= 0");

            // The stored total must always reconcile; a mismatch is a data-integrity bug.
            table.HasCheckConstraint(
                "CK_Orders_Total_Reconciles",
                "[TotalMinor] = [SubtotalMinor] + [TaxMinor]");
            table.HasCheckConstraint(
                "CK_Orders_Currency_Uppercase",
                ColumnTypes.UppercaseCurrency(nameof(Order.Currency)));
        });

        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();

        builder.Property(order => order.Status).AsEnumString();
        builder.Property(order => order.Currency).AsCurrency();
        builder.Property(order => order.SubtotalMinor).IsRequired();
        builder.Property(order => order.TaxMinor).IsRequired();
        builder.Property(order => order.TotalMinor).IsRequired();
        builder.Property(order => order.StripeCheckoutSessionId).HasMaxLength(128);
        builder.Property(order => order.StripePaymentIntentId).HasMaxLength(128);
        builder.Property(order => order.PaidAtUtc).AsTimestamp();
        builder.Property(order => order.CreatedAtUtc).AsTimestamp();
        builder.Property(order => order.UpdatedAtUtc).AsTimestamp();
        builder.Property(order => order.RowVersion).IsRowVersion();

        builder.HasOne(order => order.User)
            .WithMany()
            .HasForeignKey(order => order.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(order => order.StripeCheckoutSessionId)
            .IsUnique()
            .HasFilter("[StripeCheckoutSessionId] IS NOT NULL")
            .HasDatabaseName("UX_Orders_StripeCheckoutSessionId");

        builder.HasIndex(order => order.StripePaymentIntentId)
            .IsUnique()
            .HasFilter("[StripePaymentIntentId] IS NOT NULL")
            .HasDatabaseName("UX_Orders_StripePaymentIntentId");

        builder.HasIndex(order => new { order.UserId, order.CreatedAtUtc })
            .HasDatabaseName("IX_Orders_UserId_CreatedAtUtc");
    }
}
