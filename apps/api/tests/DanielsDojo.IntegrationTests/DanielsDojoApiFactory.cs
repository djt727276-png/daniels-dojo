using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DanielsDojo.IntegrationTests;

/// <summary>
/// Boots the real API host in a production-flavoured environment. Running the host
/// as Production verifies that Development-only behaviour (such as the OpenAPI
/// document) is never accidentally assumed by the integration suite.
/// </summary>
public sealed class DanielsDojoApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Environment name the host runs under for these tests.</summary>
    public const string ExpectedEnvironment = "Production";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(ExpectedEnvironment);
    }
}
