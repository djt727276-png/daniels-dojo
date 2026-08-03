using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DanielsDojo.IntegrationTests;

/// <summary>
/// Boots the real API host in a non-Development environment with authentication left disabled.
/// Running outside Development verifies that Development-only behaviour (such as the OpenAPI
/// document) is never accidentally assumed by the integration suite.
/// </summary>
/// <remarks>
/// Staging rather than Production on purpose: configuration validation refuses to start a
/// Production host with authentication disabled, so exercising disabled mode requires a
/// non-Production environment name. That refusal is covered by
/// <c>Authentication.AuthenticationConfigurationTests</c>.
/// </remarks>
public sealed class DanielsDojoApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Environment name the host runs under for these tests.</summary>
    public const string ExpectedEnvironment = "Staging";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(ExpectedEnvironment);
    }
}
