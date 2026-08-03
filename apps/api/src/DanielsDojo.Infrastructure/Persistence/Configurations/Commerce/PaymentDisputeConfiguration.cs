using DanielsDojo.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Commerce;

/// <summary>Maps <see cref="PaymentDispute"/> to <c>commerce.PaymentDisputes</c>.</summary>
internal sealed class PaymentDisputeConfiguration : IEntityTypeConfiguration<PaymentDispute>
{
    public void Configure(EntityTypeBuilder<PaymentDispute> builder)
    {
        builder.ToTable("PaymentDisputes", DatabaseSchemas.Commerce, table =>
        {
            table.HasCheckConstraint(
                "CK_PaymentDisputes_Status",
                ColumnTypes.EnumValues<PaymentDisputeStatus>(nameof(PaymentDispute.Status)));
            table.HasCheckConstraint(
                "CK_PaymentDisputes_AmountMinor_Positive",
                "[AmountMinor] > 0");
            table.HasCheckConstraint(
                "CK_PaymentDisputes_Currency_Uppercase",
                ColumnTypes.UppercaseCurrency(nameof(PaymentDispute.Currency)));

            // A dispute belongs to exactly one commerce record — never both, never neither.
            table.HasCheckConstraint(
                "CK_PaymentDisputes_ExactlyOneSource",
                "([OrderId] IS NOT NULL AND [SubscriptionId] IS NULL) " +
                "OR ([OrderId] IS NULL AND [SubscriptionId] IS NOT NULL)");
        });

        builder.HasKey(dispute => dispute.Id);
        builder.Property(dispute => dispute.Id).ValueGeneratedNever();

        builder.Property(dispute => dispute.StripeDisputeId).HasMaxLength(128).IsRequired();
        builder.Property(dispute => dispute.StripeChargeId).HasMaxLength(128).IsRequired();
        builder.Property(dispute => dispute.AmountMinor).IsRequired();
        builder.Property(dispute => dispute.Currency).AsCurrency();
        builder.Property(dispute => dispute.Status).AsEnumString();
        builder.Property(dispute => dispute.Reason).HasMaxLength(256).IsRequired();
        builder.Property(dispute => dispute.ResolvedAtUtc).AsTimestamp();
        builder.Property(dispute => dispute.CreatedAtUtc).AsTimestamp();
        builder.Property(dispute => dispute.UpdatedAtUtc).AsTimestamp();
        builder.Property(dispute => dispute.RowVersion).IsRowVersion();

        builder.HasOne(dispute => dispute.Order)
            .WithMany()
            .HasForeignKey(dispute => dispute.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(dispute => dispute.Subscription)
            .WithMany()
            .HasForeignKey(dispute => dispute.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(dispute => dispute.StripeDisputeId)
            .IsUnique()
            .HasDatabaseName("UX_PaymentDisputes_StripeDisputeId");

        builder.HasIndex(dispute => dispute.Status)
            .HasDatabaseName("IX_PaymentDisputes_Status");
    }
}
