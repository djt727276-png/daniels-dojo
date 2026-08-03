using System.Net;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DanielsDojo.IntegrationTests.Database;

/// <summary>
/// Proves the readiness probe reflects real database reachability: unhealthy without SQL and
/// healthy against the migrated container database, while liveness stays independent of both.
/// </summary>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class DatabaseHealthTests(SqlServerDatabaseFixture fixture)
{
    [Fact]
    public async Task Readiness_IsHealthy_AgainstTheMigratedDatabase()
    {
        using ConfiguredApiFactory factory = new(fixture.ConnectionString);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Liveness_IsHealthy_AgainstTheMigratedDatabase()
    {
        using ConfiguredApiFactory factory = new(fixture.ConnectionString);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Credential-free, non-routable placeholder: loopback port nothing listens on, integrated
    /// security, one-second timeout. Shared with the design-time factory so no test carries a
    /// username or password literal.
    /// </summary>
    private static string UnreachableConnectionString =>
        DanielsDojoDbContextFactory.BuildModelOnlyPlaceholderConnectionString();

    [Fact]
    public async Task Readiness_IsUnhealthy_WhenPointedAtAnUnreachableServer()
    {
        using ConfiguredApiFactory factory = new(UnreachableConnectionString);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task SystemStatus_StillRespondsWithoutTouchingTheDatabase()
    {
        using ConfiguredApiFactory factory = new(UnreachableConnectionString);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/system/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Boots the real API host with a supplied connection string.
    /// </summary>
    /// <remarks>
    /// The value is supplied through the standard <c>ConnectionStrings__*</c> environment
    /// variable rather than <c>ConfigureAppConfiguration</c>. Program.cs reads configuration
    /// while the top-level statements run, which is before WebApplicationFactory's
    /// configuration callbacks execute, so an in-memory source would arrive too late to reach
    /// <c>AddInfrastructure</c>. The environment variable exercises the same path the
    /// container and CI use. The database suite runs without parallelisation, so mutating
    /// process environment state here is safe.
    /// </remarks>
    private sealed class ConfiguredApiFactory : WebApplicationFactory<Program>
    {
        private const string ConnectionStringVariable = "ConnectionStrings__DanielsDojoDatabase";

        private readonly string? _previousValue;

        public ConfiguredApiFactory(string connectionString)
        {
            _previousValue = Environment.GetEnvironmentVariable(ConnectionStringVariable);
            Environment.SetEnvironmentVariable(ConnectionStringVariable, connectionString);
        }

        // Staging, not Production: these tests exercise authentication-disabled mode, which a
        // Production host deliberately refuses to start in.
        protected override void ConfigureWebHost(IWebHostBuilder builder)
            => builder.UseEnvironment("Staging");

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Environment.SetEnvironmentVariable(ConnectionStringVariable, _previousValue);
            }

            base.Dispose(disposing);
        }
    }
}
