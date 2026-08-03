using DanielsDojo.Domain.Auditing;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Domain.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DanielsDojo.Infrastructure.Persistence;

/// <summary>
/// The Daniel's Dojo relational context. Mapping lives entirely in
/// <see cref="IEntityTypeConfiguration{TEntity}"/> classes discovered by assembly scan;
/// <see cref="OnModelCreating"/> applies that assembly and nothing else.
/// </summary>
/// <remarks>
/// There are deliberately no global query filters: a filter that hid rows could conceal a
/// purchased entitlement or an audited action.
/// </remarks>
public sealed class DanielsDojoDbContext(DbContextOptions<DanielsDojoDbContext> options)
    : DbContext(options)
{
    /// <summary>Platform users.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Authorization roles.</summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>Role assignments.</summary>
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    /// <summary>Append-only audit trail.</summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>Courses.</summary>
    public DbSet<Course> Courses => Set<Course>();

    /// <summary>Course sections.</summary>
    public DbSet<CourseSection> CourseSections => Set<CourseSection>();

    /// <summary>Lessons.</summary>
    public DbSet<Lesson> Lessons => Set<Lesson>();

    /// <summary>Lesson video provider metadata.</summary>
    public DbSet<LessonVideo> LessonVideos => Set<LessonVideo>();

    /// <summary>Lesson downloadable resources.</summary>
    public DbSet<LessonResource> LessonResources => Set<LessonResource>();

    /// <summary>Catalog tags.</summary>
    public DbSet<Tag> Tags => Set<Tag>();

    /// <summary>Course-to-tag assignments.</summary>
    public DbSet<CourseTag> CourseTags => Set<CourseTag>();

    /// <summary>Course-to-instructor attributions.</summary>
    public DbSet<CourseInstructor> CourseInstructors => Set<CourseInstructor>();

    /// <summary>Purchasable offers.</summary>
    public DbSet<Offer> Offers => Set<Offer>();

    /// <summary>Published prices.</summary>
    public DbSet<Price> Prices => Set<Price>();

    /// <summary>Links between users and payment-provider customers.</summary>
    public DbSet<StripeCustomer> StripeCustomers => Set<StripeCustomer>();

    /// <summary>One-time purchases.</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>Purchased order lines.</summary>
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    /// <summary>Recurring memberships.</summary>
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    /// <summary>Access grants.</summary>
    public DbSet<Entitlement> Entitlements => Set<Entitlement>();

    /// <summary>Inbound provider webhook events.</summary>
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();

    /// <summary>Issued refunds.</summary>
    public DbSet<Refund> Refunds => Set<Refund>();

    /// <summary>Payment disputes and chargebacks.</summary>
    public DbSet<PaymentDispute> PaymentDisputes => Set<PaymentDispute>();

    /// <summary>Course enrollments.</summary>
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();

    /// <summary>Per-lesson progress.</summary>
    public DbSet<LessonProgress> LessonProgress => Set<LessonProgress>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DanielsDojoDbContext).Assembly);
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NormalizeTimestampsToUtc();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        NormalizeTimestampsToUtc();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Rewrites every tracked <see cref="DateTimeOffset"/> to UTC before it reaches the
    /// database, so stored values always carry offset zero regardless of the offset the
    /// caller happened to construct.
    /// </summary>
    private void NormalizeTimestampsToUtc()
    {
        foreach (EntityEntry entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            foreach (PropertyEntry property in entry.Properties)
            {
                switch (property.CurrentValue)
                {
                    case DateTimeOffset value when value.Offset != TimeSpan.Zero:
                        property.CurrentValue = value.ToUniversalTime();
                        break;
                }
            }
        }
    }
}
