using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace DanielsDojo.Api.Hosting;

/// <summary>
/// Explicit operator entry point for migrating and seeding the database. It reuses the API's
/// own dependency-injection composition, so there is no second host project and no drift in
/// how the context is configured.
/// </summary>
/// <remarks>
/// This runs only when the process is started with the <c>database</c> argument, and it
/// always exits instead of serving traffic. Ordinary API startup never migrates or seeds.
/// </remarks>
internal static partial class DatabaseCommand
{
    /// <summary>First argument that selects the database command instead of the web server.</summary>
    public const string CommandName = "database";

    private const string MigrateVerb = "migrate";
    private const string SeedVerb = "seed";
    private const string ProfileOption = "--profile";

    /// <summary>Whether the supplied arguments select the database command.</summary>
    public static bool Matches(string[] args)
        => args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.Ordinal);

    /// <summary>
    /// Runs the requested database operation and returns a process exit code. Zero means
    /// success; any non-zero value means the caller should treat the run as failed.
    /// </summary>
    public static async Task<int> ExecuteAsync(WebApplication app, string[] args)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length < 2)
        {
            WriteUsage("A verb is required.");
            return 1;
        }

        string verb = args[1];
        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseCommand).FullName!);

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();

        try
        {
            switch (verb)
            {
                case MigrateVerb:
                    await MigrateAsync(scope, logger).ConfigureAwait(false);

                    // 'migrate --profile <name>' seeds in the same run, which is what the
                    // local bootstrap scripts use.
                    if (TryReadProfile(args, out SeedProfile migrateProfile, out string? migrateError))
                    {
                        await SeedAsync(scope, migrateProfile, logger).ConfigureAwait(false);
                    }
                    else if (migrateError is not null)
                    {
                        WriteUsage(migrateError);
                        return 1;
                    }

                    return 0;

                case SeedVerb:
                    if (!TryReadProfile(args, out SeedProfile seedProfile, out string? seedError))
                    {
                        WriteUsage(seedError ?? $"'{SeedVerb}' requires {ProfileOption} <reference|development>.");
                        return 1;
                    }

                    await SeedAsync(scope, seedProfile, logger).ConfigureAwait(false);
                    return 0;

                default:
                    WriteUsage($"Unknown verb '{verb}'.");
                    return 1;
            }
        }
        catch (InvalidOperationException exception)
        {
            // Covers the environment guard and a missing connection string: both are operator
            // errors that deserve a readable message rather than a stack trace.
            await Console.Error.WriteLineAsync($"Database command failed: {exception.Message}")
                .ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task MigrateAsync(AsyncServiceScope scope, ILogger logger)
    {
        DanielsDojoDbContext context =
            scope.ServiceProvider.GetRequiredService<DanielsDojoDbContext>();

        IEnumerable<string> pending =
            await context.Database.GetPendingMigrationsAsync().ConfigureAwait(false);
        string[] pendingList = [.. pending];

        if (pendingList.Length == 0)
        {
            LogNoPendingMigrations(logger);
            return;
        }

        string migrationNames = string.Join(", ", pendingList);
        LogApplyingMigrations(logger, pendingList.Length, migrationNames);
        await context.Database.MigrateAsync().ConfigureAwait(false);
        LogMigrationsApplied(logger);
    }

    private static async Task SeedAsync(AsyncServiceScope scope, SeedProfile profile, ILogger logger)
    {
        DatabaseSeeder seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();

        // Fail before touching the database when the profile is not allowed here.
        seeder.GuardProfileAllowed(profile);

        LogSeeding(logger, profile);
        await seeder.SeedAsync(profile).ConfigureAwait(false);
    }

    private static bool TryReadProfile(string[] args, out SeedProfile profile, out string? error)
    {
        profile = SeedProfile.Reference;
        error = null;

        int index = Array.IndexOf(args, ProfileOption);
        if (index < 0)
        {
            return false;
        }

        if (index + 1 >= args.Length)
        {
            error = $"{ProfileOption} requires a value of 'reference' or 'development'.";
            return false;
        }

        string value = args[index + 1];
        if (Enum.TryParse(value, ignoreCase: true, out profile) && Enum.IsDefined(profile))
        {
            return true;
        }

        error = $"Unknown seed profile '{value}'. Use 'reference' or 'development'.";
        return false;
    }

    private static void WriteUsage(string problem)
    {
        Console.Error.WriteLine(problem);
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  database migrate");
        Console.Error.WriteLine("  database migrate --profile <reference|development>");
        Console.Error.WriteLine("  database seed --profile <reference|development>");
    }

    [LoggerMessage(EventId = 2100, Level = LogLevel.Information,
        Message = "Database is already up to date; no migrations pending.")]
    private static partial void LogNoPendingMigrations(ILogger logger);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Information,
        Message = "Applying {Count} pending migration(s): {Migrations}.")]
    private static partial void LogApplyingMigrations(ILogger logger, int count, string migrations);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Information,
        Message = "Migrations applied successfully.")]
    private static partial void LogMigrationsApplied(ILogger logger);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Information,
        Message = "Seeding with the {Profile} profile.")]
    private static partial void LogSeeding(ILogger logger, SeedProfile profile);
}
