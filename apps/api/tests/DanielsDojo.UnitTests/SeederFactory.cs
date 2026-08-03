using DanielsDojo.Application.System;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DanielsDojo.UnitTests;

/// <summary>
/// Builds a seeder for guard tests. The context is configured against SQL Server but never
/// connects: <see cref="DatabaseSeeder.GuardProfileAllowed"/> is a pure environment check
/// that must fail before any connection is attempted.
/// </summary>
internal static class SeederFactory
{
    public static DatabaseSeeder Create(string environmentName)
    {
        DbContextOptionsBuilder<DanielsDojoDbContext> options = new();
        options.UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True");

        return new DatabaseSeeder(
            new DanielsDojoDbContext(options.Options),
            new StubApplicationEnvironment(environmentName),
            TimeProvider.System,
            NullLogger<DatabaseSeeder>.Instance);
    }

    private sealed class StubApplicationEnvironment(string environmentName) : IApplicationEnvironment
    {
        public string EnvironmentName { get; } = environmentName;
    }
}
