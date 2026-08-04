using DanielsDojo.Application.Catalog;
using DanielsDojo.Application.Identity;
using DanielsDojo.Infrastructure.Catalog;
using DanielsDojo.Infrastructure.Identity;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DanielsDojo.Infrastructure;

/// <summary>Composition root for Infrastructure services.</summary>
public static class DependencyInjection
{
    /// <summary>Health check name for the database readiness probe.</summary>
    public const string DatabaseHealthCheckName = "database";

    /// <summary>Tag marking checks that gate readiness rather than liveness.</summary>
    public const string ReadinessTag = "ready";

    /// <summary>
    /// Registers persistence. Registration never opens a connection, migrates, creates, or
    /// seeds: those are explicit operator actions. A missing connection string is therefore
    /// not a startup failure — the process still starts and reports liveness, and the
    /// readiness probe is the thing that turns unhealthy.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string? connectionString =
            configuration.GetConnectionString(DatabaseConventions.ConnectionStringName);

        services.AddDbContext<DanielsDojoDbContext>(options =>
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                // Deferred: the provider is configured but has no connection string, so any
                // attempt to use it fails with EF Core's own actionable message.
                options.UseSqlServer(ConfigureSqlServer);
            }
            else
            {
                options.UseSqlServer(connectionString, ConfigureSqlServer);
            }
        });

        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();
        services.AddScoped<AdminRoleGrantService>();
        services.AddScoped<IPublicCatalogQueries, PublicCatalogQueries>();

        return services;
    }

    /// <summary>
    /// Adds the readiness database probe. Readiness requires both a reachable database and a
    /// fully applied migration set, so a stale schema is reported as not ready.
    /// </summary>
    public static IHealthChecksBuilder AddDatabaseReadinessCheck(this IHealthChecksBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddDbContextCheck<DanielsDojoDbContext>(
            name: DatabaseHealthCheckName,
            failureStatus: HealthStatus.Unhealthy,
            tags: [ReadinessTag],
            customTestQuery: async (context, cancellationToken) =>
            {
                IEnumerable<string> pending =
                    await context.Database.GetPendingMigrationsAsync(cancellationToken)
                        .ConfigureAwait(false);

                return !pending.Any();
            });
    }

    private static void ConfigureSqlServer(
        Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder sqlServer)
        => sqlServer.MigrationsHistoryTable(
            DatabaseConventions.MigrationsHistoryTable,
            DatabaseSchemas.Infrastructure);
}
