using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.UnitTests.Identity;

public sealed class DeviceSessionPolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void EnsureCanCreateSession_ShouldAllowCountBelowMaximum(
        int activeSessionCount)
    {
        DeviceSessionPolicy
            .EnsureCanCreateSession(
                activeSessionCount);
    }

    [Fact]
    public void EnsureCanCreateSession_ShouldRejectMaximumCount()
    {
        Assert.Throws<DomainException>(
            () => DeviceSessionPolicy
                .EnsureCanCreateSession(
                    DeviceSessionPolicy
                        .MaximumActiveSessions));
    }

    [Fact]
    public void EnsureCanCreateSession_ShouldRejectCountAboveMaximum()
    {
        Assert.Throws<DomainException>(
            () => DeviceSessionPolicy
                .EnsureCanCreateSession(
                    DeviceSessionPolicy
                        .MaximumActiveSessions + 1));
    }

    [Fact]
    public void EnsureCanCreateSession_ShouldRejectNegativeCount()
    {
        Assert.Throws<DomainException>(
            () => DeviceSessionPolicy
                .EnsureCanCreateSession(-1));
    }
}