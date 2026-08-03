using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DanielsDojo.Infrastructure.Persistence;

/// <summary>
/// Supplies a context to the <c>dotnet ef</c> tooling without booting the web host.
/// The real connection string comes from the environment only. When it is absent the factory
/// falls back to a credential-free, non-routable placeholder so model-only commands still
/// work — no username, password, token, or production-shaped string is ever embedded here.
/// </summary>
public sealed class DanielsDojoDbContextFactory : IDesignTimeDbContextFactory<DanielsDojoDbContext>
{
    /// <summary>Environment variable read at design time.</summary>
    public const string ConnectionStringEnvironmentVariable = "DANIELSDOJO_DB_CONNECTION";

    /// <summary>Loopback host and port the placeholder points at. Nothing listens there.</summary>
    private const string ModelOnlyDataSource = "127.0.0.1,14399";

    /// <summary>Catalog name used by the placeholder. No such database is ever created.</summary>
    private const string ModelOnlyInitialCatalog = "DanielsDojoDesignTime";

    /// <summary>
    /// Builds the credential-free placeholder used when no real connection string is
    /// configured. It carries no username or password — it authenticates with integrated
    /// security against a loopback port nothing listens on, and times out in one second.
    /// EF only needs a syntactically valid string to build the model; any command that
    /// actually connects fails fast instead of reaching a real server.
    /// </summary>
    public static string BuildModelOnlyPlaceholderConnectionString()
    {
        SqlConnectionStringBuilder builder = new()
        {
            DataSource = ModelOnlyDataSource,
            InitialCatalog = ModelOnlyInitialCatalog,
            IntegratedSecurity = true,
            Encrypt = true,
            TrustServerCertificate = true,
            ConnectTimeout = 1,
        };

        return builder.ConnectionString;
    }

    /// <inheritdoc />
    public DanielsDojoDbContext CreateDbContext(string[] args)
    {
        string? connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);

        DbContextOptionsBuilder<DanielsDojoDbContext> optionsBuilder = new();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Model-only commands — 'migrations add', 'migrations list --no-connect',
            // 'has-pending-model-changes', and 'migrations script' — need the model but not a
            // database, and CI must be able to run them without one. The placeholder is
            // credential-free and points at a loopback port nothing listens on, so a command
            // that does try to connect fails in about a second rather than reaching a server.
            //
            // Deliberately stdout, not stderr: for model-only commands this is guidance, not a
            // failure, and Windows PowerShell turns native stderr into a terminating error
            // whenever the caller redirects the pipeline.
            Console.Out.WriteLine(
                $"[design-time] {ConnectionStringEnvironmentVariable} is not set. Using a " +
                $"credential-free placeholder ({ModelOnlyDataSource}, integrated security, no " +
                "credentials) so model-only EF commands work. Any command that reaches the " +
                "database will fail against it. To run a real database command, start the local " +
                "database with 'scripts/db.ps1 start' (PowerShell) or 'scripts/db.sh start' " +
                "(Bash), which write the credential outside the repository. No connection " +
                "string or credential is ever committed.");

            optionsBuilder.UseSqlServer(BuildModelOnlyPlaceholderConnectionString(), ConfigureSqlServer);
        }
        else
        {
            optionsBuilder.UseSqlServer(connectionString, ConfigureSqlServer);
        }

        return new DanielsDojoDbContext(optionsBuilder.Options);
    }

    private static void ConfigureSqlServer(
        Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder sqlServer)
        => sqlServer.MigrationsHistoryTable(
            DatabaseConventions.MigrationsHistoryTable,
            DatabaseSchemas.Infrastructure);
}
