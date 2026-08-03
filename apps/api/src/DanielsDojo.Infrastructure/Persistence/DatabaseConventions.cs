namespace DanielsDojo.Infrastructure.Persistence;

/// <summary>Names shared by runtime registration, design-time tooling, and test resets.</summary>
public static class DatabaseConventions
{
    /// <summary>
    /// EF Core migration history table. It lives in the <c>infrastructure</c> schema so
    /// test resets can clear application data without dropping migration state.
    /// </summary>
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    /// <summary>Configuration key holding the application's SQL Server connection string.</summary>
    public const string ConnectionStringName = "DanielsDojoDatabase";
}
