using DanielsDojo.Domain.Commerce;
using Xunit;

namespace DanielsDojo.UnitTests;

/// <summary>
/// The commerce status graph. Retirement is deliberately terminal: orders, subscriptions, and
/// entitlements reference the exact offer and price they were sold under, so reviving a
/// withdrawn one would quietly change what those records mean.
/// </summary>
public sealed class CommerceStatusGraphTests
{
    [Theory]
    [InlineData(CommerceStatus.Draft, CommerceStatus.Active)]
    [InlineData(CommerceStatus.Draft, CommerceStatus.Retired)]
    [InlineData(CommerceStatus.Active, CommerceStatus.Retired)]
    public void AllowedTransitions_AreAccepted(CommerceStatus from, CommerceStatus to) =>
        Assert.True(CommerceStatusGraph.CanTransition(from, to));

    [Theory]
    [InlineData(CommerceStatus.Retired, CommerceStatus.Active)]
    [InlineData(CommerceStatus.Retired, CommerceStatus.Draft)]
    [InlineData(CommerceStatus.Active, CommerceStatus.Draft)]
    [InlineData(CommerceStatus.Active, CommerceStatus.Active)]
    public void OtherTransitions_AreRefused(CommerceStatus from, CommerceStatus to) =>
        Assert.False(CommerceStatusGraph.CanTransition(from, to));

    [Fact]
    public void RetiredOffersNothing() =>
        Assert.Empty(CommerceStatusGraph.AllowedTargets(CommerceStatus.Retired));

    [Theory]
    [InlineData(CommerceStatus.Draft, true)]
    [InlineData(CommerceStatus.Active, false)]
    [InlineData(CommerceStatus.Retired, false)]
    public void OnlyDraftsAreEditable(CommerceStatus status, bool expected) =>
        Assert.Equal(expected, CommerceStatusGraph.IsEditable(status));
}
