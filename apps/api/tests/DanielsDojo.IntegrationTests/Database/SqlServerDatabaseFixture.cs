using DanielsDojo.Application.System;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.Infrastructure.Persistence.Seeding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Respawn;
using Testcontainers.MsSql;
using Xunit;

namespace DanielsDojo.IntegrationTests.Database;

/// <summary>
/// Boots one real SQL Server 2025 container for the database suite and applies
/// <c>InitialPlatformSchema</c> to it. Tests run against the same engine, types, and
/// constraints as production — never SQLite and never the EF in-memory provider.
/// </summary>
public sealed class SqlServerDatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();

    private Respawner? _respawner;

    /// <summary>Connection string for the running container database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Creates a context bound to the container database.</summary>
    public DanielsDojoDbContext CreateContext()
    {
        DbContextOptionsBuilder<DanielsDojoDbContext> options = new();
        options.UseSqlServer(
            ConnectionString,
            sqlServer => sqlServer.MigrationsHistoryTable(
                DatabaseConventions.MigrationsHistoryTable,
                DatabaseSchemas.Infrastructure));

        return new DanielsDojoDbContext(options.Options);
    }

    /// <summary>Creates a seeder bound to a fresh context for the given environment.</summary>
    public static DatabaseSeeder CreateSeeder(DanielsDojoDbContext context, string environmentName)
        => new(
            context,
            new FixedApplicationEnvironment(environmentName),
            TimeProvider.System,
            NullLogger<DatabaseSeeder>.Instance);

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        await using DanielsDojoDbContext context = CreateContext();
        await context.Database.MigrateAsync();

        // Respawn is scoped to the application schemas only. The infrastructure schema —
        // and therefore __EFMigrationsHistory — is deliberately excluded, so a reset clears
        // data without ever making the database look unmigrated.
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            SchemasToInclude =
            [
                DatabaseSchemas.Identity,
                DatabaseSchemas.Catalog,
                DatabaseSchemas.Commerce,
                DatabaseSchemas.Learning,
                DatabaseSchemas.Audit,
                DatabaseSchemas.Community,
                DatabaseSchemas.Media,
            ],
            DbAdapter = DbAdapter.SqlServer,
        });
    }

    /// <summary>
    /// Clears every application row while preserving the migration history, then reinstalls
    /// the reference seed so each test starts from the same known baseline.
    /// </summary>
    public async Task ResetAsync()
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await _respawner!.ResetAsync(connection);

        await using DanielsDojoDbContext context = CreateContext();
        DatabaseSeeder seeder = CreateSeeder(context, "Production");
        await seeder.SeedAsync(SeedProfile.Reference);
    }

    /// <summary>Clears every application row without reinstalling any seed data.</summary>
    public async Task ResetWithoutSeedAsync()
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await _respawner!.ResetAsync(connection);
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _container.DisposeAsync();

    private sealed class FixedApplicationEnvironment(string environmentName) : IApplicationEnvironment
    {
        public string EnvironmentName { get; } = environmentName;
    }
}

/// <summary>
/// Serialises the database suite. Every test class here shares one container and resets it
/// between tests, so they must never run in parallel against the same database.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerDatabaseSuite : ICollectionFixture<SqlServerDatabaseFixture>
{
    /// <summary>Collection name shared by every database test class.</summary>
    public const string Name = "SqlServerDatabase";
}
