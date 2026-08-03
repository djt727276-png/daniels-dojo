using System.Net;
using System.Text.Json;
using Xunit;

namespace DanielsDojo.IntegrationTests;

public sealed class SystemStatusEndpointTests(DanielsDojoApiFactory factory)
    : IClassFixture<DanielsDojoApiFactory>
{
    private static readonly string[] ExpectedPropertyNames =
        ["environment", "service", "status", "utcTimestamp"];

    private readonly DanielsDojoApiFactory _factory = factory;

    [Fact]
    public async Task Status_Returns200_WithSafeCamelCaseContract()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/system/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;

        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal("Daniel's Dojo API", root.GetProperty("service").GetString());
        Assert.Equal(
            DanielsDojoApiFactory.ExpectedEnvironment,
            root.GetProperty("environment").GetString());

        // Timestamp is a valid UTC value (zero offset).
        var timestamp = root.GetProperty("utcTimestamp").GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, timestamp.Offset);

        // The response exposes only the four safe, camel-cased fields and nothing else
        // (no machine names, versions, connection strings, or other internal detail).
        var propertyNames = root.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ExpectedPropertyNames, propertyNames);
    }

    [Fact]
    public async Task Liveness_IsHealthy_WithoutAnyDatabase()
    {
        // This host has no SQL Server. Liveness must still succeed, because a database
        // outage should never cause an orchestrator to kill a healthy process.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Readiness_IsUnhealthy_WhenTheDatabaseIsUnreachable()
    {
        // No connection string is configured for this factory, so the readiness probe must
        // report that the instance cannot serve traffic yet.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task UnknownApiRoute_DoesNotReturnSuccess()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/does-not-exist");

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_IsNotExposed_WhenNotDevelopment()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.False(response.IsSuccessStatusCode);
    }
}
