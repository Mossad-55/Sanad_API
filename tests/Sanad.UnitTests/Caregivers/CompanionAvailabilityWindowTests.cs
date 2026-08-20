using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CompanionAvailabilityWindowTests
{
    [Fact]
    public void Create_ShouldCreateSameDayWindow()
    {
        CompanionAvailabilityWindow window =
            CompanionAvailabilityWindow.Create(
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(16, 0));

        Assert.Equal(
            DayOfWeek.Saturday,
            window.DayOfWeek);

        Assert.Equal(
            new TimeOnly(8, 0),
            window.StartTime);

        Assert.Equal(
            new TimeOnly(16, 0),
            window.EndTime);

        Assert.False(window.EndsNextDay);

        Assert.Equal(
            TimeSpan.FromHours(8),
            window.Duration);
    }

    [Fact]
    public void Create_ShouldCreateOvernightWindow()
    {
        CompanionAvailabilityWindow window =
            CompanionAvailabilityWindow.Create(
                DayOfWeek.Saturday,
                new TimeOnly(20, 0),
                new TimeOnly(8, 0));

        Assert.True(window.EndsNextDay);

        Assert.Equal(
            TimeSpan.FromHours(12),
            window.Duration);
    }

    [Fact]
    public void Create_ShouldSupportCustomTimes()
    {
        CompanionAvailabilityWindow window =
            CompanionAvailabilityWindow.Create(
                DayOfWeek.Sunday,
                new TimeOnly(9, 30),
                new TimeOnly(13, 15));

        Assert.Equal(
            TimeSpan.FromHours(3) +
            TimeSpan.FromMinutes(45),
            window.Duration);
    }

    [Fact]
    public void Create_ShouldRejectEqualStartAndEndTimes()
    {
        Assert.Throws<DomainException>(
            () => CompanionAvailabilityWindow.Create(
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(8, 0)));
    }

    [Fact]
    public void Create_ShouldRejectInvalidDayOfWeek()
    {
        Assert.Throws<DomainException>(
            () => CompanionAvailabilityWindow.Create(
                (DayOfWeek)999,
                new TimeOnly(8, 0),
                new TimeOnly(16, 0)));
    }

    [Fact]
    public void EqualWindows_ShouldHaveValueEquality()
    {
        CompanionAvailabilityWindow first =
            CompanionAvailabilityWindow.Create(
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(16, 0));

        CompanionAvailabilityWindow second =
            CompanionAvailabilityWindow.Create(
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(16, 0));

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentWindows_ShouldNotBeEqual()
    {
        CompanionAvailabilityWindow first =
            CompanionAvailabilityWindow.Create(
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(16, 0));

        CompanionAvailabilityWindow second =
            CompanionAvailabilityWindow.Create(
                DayOfWeek.Saturday,
                new TimeOnly(9, 0),
                new TimeOnly(17, 0));

        Assert.NotEqual(first, second);
    }
}