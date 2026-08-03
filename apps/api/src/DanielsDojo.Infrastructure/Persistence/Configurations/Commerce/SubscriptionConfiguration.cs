using DanielsDojo.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Commerce;

/// <summary>
/// Maps <see cref="Subscription"/> to <c>commerce.Subscriptions</c>. There is deliberately no
/// unique constraint on (UserId, OfferId): a customer may subscribe, cancel, and resubscribe,
/// and every one of those rows is retained.
/// </summary>
internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("Subscriptions", DatabaseSchemas.Commerce, table =>
        {
            table.HasCheckConstraint(
                "CK_Subscriptions_Status",
                ColumnTypes.EnumValues<SubscriptionStatus>(nameof(Subscription.Status)));
            table.HasCheckConstraint(
                "CK_Subscriptions_PeriodOrdered",
                "[CurrentPeriodEndUtc] >= [CurrentPeriodStartUtc]");
            table.HasCheckConstraint(
                "CK_Subscriptions_TrialOrdered",
                "[TrialStartUtc] IS NULL OR [TrialEndUtc] IS NULL OR [TrialEndUtc] >= [TrialStartUtc]");
        });

        builder.HasKey(subscription => subscription.Id);
        builder.Property(subscription => subscription.Id).ValueGeneratedNever();

        builder.Property(subscription => subscription.StripeSubscriptionId).HasMaxLength(128).IsRequired();
        builder.Property(subscription => subscription.Status).AsEnumString();
        builder.Property(subscription => subscription.CurrentPeriodStartUtc).AsTimestamp();
        builder.Property(subscription => subscription.CurrentPeriodEndUtc).AsTimestamp();
        builder.Property(subscription => subscription.CancelAtPeriodEnd).IsRequired();
        builder.Property(subscription => subscription.CanceledAtUtc).AsTimestamp();
        builder.Property(subscription => subscription.EndedAtUtc).AsTimestamp();
        builder.Property(subscription => subscription.TrialStartUtc).AsTimestamp();
        builder.Property(subscription => subscription.TrialEndUtc).AsTimestamp();
        builder.Property(subscription => subscription.FirstPaymentFailedAtUtc).AsTimestamp();
        builder.Property(subscription => subscription.GracePeriodEndsAtUtc).AsTimestamp();
        builder.Property(subscription => subscription.CreatedAtUtc).AsTimestamp();
        builder.Property(subscription => subscription.UpdatedAtUtc).AsTimestamp();
        builder.Property(subscription => subscription.RowVersion).IsRowVersion();

        builder.HasOne(subscription => subscription.User)
            .WithMany()
            .HasForeignKey(subscription => subscription.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(subscription => subscription.Offer)
            .WithMany()
            .HasForeignKey(subscription => subscription.OfferId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(subscription => subscription.Price)
            .WithMany()
            .HasForeignKey(subscription => subscription.PriceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(subscription => subscription.StripeSubscriptionId)
            .IsUnique()
            .HasDatabaseName("UX_Subscriptions_StripeSubscriptionId");

        builder.HasIndex(subscription => new { subscription.UserId, subscription.Status })
            .HasDatabaseName("IX_Subscriptions_UserId_Status");
    }
}
