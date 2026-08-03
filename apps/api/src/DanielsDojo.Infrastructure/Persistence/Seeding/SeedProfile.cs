namespace DanielsDojo.Infrastructure.Persistence.Seeding;

/// <summary>Which set of rows a seed run installs.</summary>
public enum SeedProfile
{
    /// <summary>
    /// Rows the platform cannot function without: roles, the launch course shell, and the
    /// launch offers and prices. Safe to run in any environment and safe to rerun.
    /// </summary>
    Reference,

    /// <summary>
    /// The reference profile plus local-only sample authoring data. Permitted only when the
    /// host environment is exactly "Development".
    /// </summary>
    Development,
}
