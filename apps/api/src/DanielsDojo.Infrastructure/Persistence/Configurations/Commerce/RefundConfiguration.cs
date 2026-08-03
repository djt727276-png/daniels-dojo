using DanielsDojo.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Commerce;

/// <summary>Maps <see cref="Refund"/> to <c>commerce.Refunds</c>.</summary>
internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("Refunds", DatabaseSchemas.Commerce, table =>
        {
            table.HasCheckConstraint(
                "CK_Refunds_Status",
                ColumnTypes.EnumValues<RefundStatus>(nameof(Refund.Status)));
            table.HasCheckConstraint(
                "CK_Refunds_AmountMinor_Positive",
                "[AmountMinor] > 0");
            table.HasCheckConstraint(
                "CK_Refunds_Currency_Uppercase",
                ColumnTypes.UppercaseCurrency(nameof(Refund.Currency)));

            // A refund belongs to exactly one commerce record — never both, never neither.
            table.HasCheckConstraint(
                "CK_Refunds_ExactlyOneSource",
                "([OrderId] IS NOT NULL AND [SubscriptionId] IS NULL) " +
                "OR ([OrderId] IS NULL AND [SubscriptionId] IS NOT NULL)");
        });

        builder.HasKey(refund => refund.Id);
        builder.Property(refund => refund.Id).ValueGeneratedNever();

        builder.Property(refund => refund.StripeRefundId).HasMaxLength(128).IsRequired();
        builder.Property(refund => refund.StripePaymentIntentId).HasMaxLength(128).IsRequired();
        builder.Property(refund => refund.AmountMinor).IsRequired();
        builder.Property(refund => refund.Currency).AsCurrency();
        builder.Property(refund => refund.Status).AsEnumString();
        builder.Property(refund => refund.Reason).HasMaxLength(256).IsRequired();
        builder.Property(refund => refund.IsFullRefund).IsRequired();
        builder.Property(refund => refund.RequiresAccessReview).IsRequired();
        builder.Property(refund => refund.OccurredAtUtc).AsTimestamp();
        builder.Property(refund => refund.CreatedAtUtc).AsTimestamp();
        builder.Property(refund => refund.UpdatedAtUtc).AsTimestamp();
        builder.Property(refund => refund.RowVersion).IsRowVersion();

        builder.HasOne(refund => refund.Order)
            .WithMany()
            .HasForeignKey(refund => refund.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(refund => refund.Subscription)
            .WithMany()
            .HasForeignKey(refund => refund.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(refund => refund.StripeRefundId)
            .IsUnique()
            .HasDatabaseName("UX_Refunds_StripeRefundId");

        builder.HasIndex(refund => refund.RequiresAccessReview)
            .HasDatabaseName("IX_Refunds_RequiresAccessReview");
    }
}
