using DanielsDojo.Application.System;
using Xunit;

namespace DanielsDojo.UnitTests;

public sealed class SystemStatusServiceTests
{
    private static readonly DateTimeOffset FixedInstant =
        new(2026, 8, 3, 12, 30, 45, TimeSpan.Zero);

    [Fact]
    public void GetStatus_ReportsOkStatusAndStableServiceName()
    {
        var status = CreateSut("Development").GetStatus();

        Assert.Equal(SystemStatusService.OkStatus, status.Status);
        Assert.Equal("ok", status.Status);
        Assert.Equal(SystemStatusService.ServiceName, status.Service);
        Assert.Equal("Daniel's Dojo API", status.Service);
    }

    [Fact]
    public void GetStatus_UsesEnvironmentNameFromAbstraction()
    {
        var status = CreateSut("Staging").GetStatus();

        Assert.Equal("Staging", status.Environment);
    }

    [Fact]
    public void GetStatus_UsesInjectedUtcTime_AndReportsUtcKind()
    {
        var status = CreateSut("Development").GetStatus();

        Assert.Equal(FixedInstant.UtcDateTime, status.UtcTimestamp);
        Assert.Equal(DateTimeKind.Utc, status.UtcTimestamp.Kind);
    }

    private static SystemStatusService CreateSut(string environmentName) =>
        new(new FixedTimeProvider(FixedInstant), new StubApplicationEnvironment(environmentName));
}
