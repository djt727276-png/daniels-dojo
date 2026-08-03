using DanielsDojo.Application.System;

namespace DanielsDojo.Api.Hosting;

/// <summary>
/// Bridges the ASP.NET Core <see cref="IHostEnvironment"/> to the Application-level
/// <see cref="IApplicationEnvironment"/> abstraction so the Application layer stays
/// free of hosting dependencies.
/// </summary>
internal sealed class HostApplicationEnvironment(IHostEnvironment hostEnvironment) : IApplicationEnvironment
{
    /// <inheritdoc />
    public string EnvironmentName => hostEnvironment.EnvironmentName;
}
