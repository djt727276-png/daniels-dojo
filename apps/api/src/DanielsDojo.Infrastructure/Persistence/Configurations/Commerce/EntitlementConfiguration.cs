using DanielsDojo.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Commerce;

/// <summary>
/// Maps <see cref="Entitlement"/> to <c>commerce.Entitlements</c>. Access is the most
/// security-sensitive record in the system, so scope and source consistency is enforced by
/// the database rather than trusted to application code.
/// </summary>
internal sealed class EntitlementConfiguration : IEntityTypeConfiguration<Entitlement>
{
    public void Configure(EntityTypeBuilder<Entitlement> builder)
    {
        builder.ToTable("Entitlements", DatabaseSchemas.Commerce, table =>
        {
            table.HasCheckConstraint(
                "CK_Entitlements_Scope",
                ColumnTypes.EnumValues<EntitlementScope>(nameof(Entitlement.Scope)));
            table.HasCheckConstraint(
                "CK_Entitlements_Source",
                ColumnTypes.EnumValues<EntitlementSource>(nameof(Entitlement.Source)));
            table.HasCheckConstraint(
                "CK_Entitlements_Status",
                ColumnTypes.EnumValues<EntitlementStatus>(nameof(Entitlement.Status)));

            // Scope determines whether a course is named.
            table.HasCheckConstraint(
                "CK_Entitlements_CourseScopeRequiresCourse",
                "[Scope] <> 'Course' OR [CourseId] IS NOT NULL");
            table.HasCheckConstraint(
                "CK_Entitlements_MembershipScopeForbidsCourse",
                "[Scope] <> 'AllMembershipCourses' OR [CourseId] IS NULL");

            // Source determines exactly which commerce record backs the grant.
            table.HasCheckConstraint(
                "CK_Entitlements_SubscriptionSource",
                "[Source] <> 'Subscription' OR ([SubscriptionId] IS NOT NULL AND [OrderItemId] IS NULL)");
            table.HasCheckConstraint(
                "CK_Entitlements_PurchaseSource",
                "[Source] <> 'Purchase' OR ([OrderItemId] IS NOT NULL AND [SubscriptionId] IS NULL)");
            table.HasCheckConstraint(
                "CK_Entitlements_ManualSource",
                "[Source] <> 'Manual' OR ([SubscriptionId] IS NULL AND [OrderItemId] IS NULL)");

            table.HasCheckConstraint(
                "CK_Entitlements_EndsAfterStarts",
                "[EndsAtUtc] IS NULL OR [EndsAtUtc] >= [StartsAtUtc]");
        });

        builder.HasKey(entitlement => entitlement.Id);
        builder.Property(entitlement => entitlement.Id).ValueGeneratedNever();

        builder.Property(entitlement => entitlement.Scope).AsEnumString();
        builder.Property(entitlement => entitlement.Source).AsEnumString();
        builder.Property(entitlement => entitlement.Status).AsEnumString();
        builder.Property(entitlement => entitlement.StartsAtUtc).AsTimestamp();
        builder.Property(entitlement => entitlement.EndsAtUtc).AsTimestamp();
        builder.Property(entitlement => entitlement.GrantReason).HasMaxLength(512);
        builder.Property(entitlement => entitlement.RevokedAtUtc).AsTimestamp();
        builder.Property(entitlement => entitlement.RevocationReason).HasMaxLength(512);
        builder.Property(entitlement => entitlement.CreatedAtUtc).AsTimestamp();
        builder.Property(entitlement => entitlement.UpdatedAtUtc).AsTimestamp();
        builder.Property(entitlement => entitlement.RowVersion).IsRowVersion();

        builder.HasOne(entitlement => entitlement.User)
            .WithMany()
            .HasForeignKey(entitlement => entitlement.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(entitlement => entitlement.Course)
            .WithMany()
            .HasForeignKey(entitlement => entitlement.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(entitlement => entitlement.Subscription)
            .WithMany()
            .HasForeignKey(entitlement => entitlement.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(entitlement => entitlement.OrderItem)
            .WithMany()
            .HasForeignKey(entitlement => entitlement.OrderItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(entitlement => entitlement.GrantedByUser)
            .WithMany()
            .HasForeignKey(entitlement => entitlement.GrantedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(entitlement => entitlement.RevokedByUser)
            .WithMany()
            .HasForeignKey(entitlement => entitlement.RevokedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // One commerce record grants at most one entitlement, so a duplicated webhook or a
        // retried checkout cannot mint a second grant.
        builder.HasIndex(entitlement => entitlement.SubscriptionId)
            .IsUnique()
            .HasFilter("[SubscriptionId] IS NOT NULL")
            .HasDatabaseName("UX_Entitlements_SubscriptionId");

        builder.HasIndex(entitlement => entitlement.OrderItemId)
            .IsUnique()
            .HasFilter("[OrderItemId] IS NOT NULL")
            .HasDatabaseName("UX_Entitlements_OrderItemId");

        builder.HasIndex(entitlement => new { entitlement.UserId, entitlement.Status })
            .HasDatabaseName("IX_Entitlements_UserId_Status");

        builder.HasIndex(entitlement => new { entitlement.UserId, entitlement.CourseId, entitlement.Status })
            .HasDatabaseName("IX_Entitlements_UserId_CourseId_Status");
    }
}
