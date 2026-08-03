using DanielsDojo.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Commerce;

/// <summary>Maps <see cref="StripeCustomer"/> to <c>commerce.StripeCustomers</c>.</summary>
internal sealed class StripeCustomerConfiguration : IEntityTypeConfiguration<StripeCustomer>
{
    public void Configure(EntityTypeBuilder<StripeCustomer> builder)
    {
        builder.ToTable("StripeCustomers", DatabaseSchemas.Commerce);

        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).ValueGeneratedNever();

        builder.Property(customer => customer.StripeCustomerId).HasMaxLength(128).IsRequired();
        builder.Property(customer => customer.CreatedAtUtc).AsTimestamp();
        builder.Property(customer => customer.UpdatedAtUtc).AsTimestamp();

        builder.HasOne(customer => customer.User)
            .WithMany()
            .HasForeignKey(customer => customer.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(customer => customer.UserId)
            .IsUnique()
            .HasDatabaseName("UX_StripeCustomers_UserId");

        builder.HasIndex(customer => customer.StripeCustomerId)
            .IsUnique()
            .HasDatabaseName("UX_StripeCustomers_StripeCustomerId");
    }
}
