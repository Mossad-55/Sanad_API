using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CompanionAvailabilityWindowTests
{
    [Fact]
    public void Create_ShouldAllowCustomHourlyWindow()
    {
        CompanionAvailabilityWindow window =
            CompanionAvailabilityWindow.Create(
                CompanionBookingType.Hourly,
                DayOfWeek.Saturday,
                new TimeOnly(9, 30),
                new TimeOnly(13, 15));

        Assert.Equal(
            CompanionBookingType.Hourly,
            window.BookingType);

        Assert.Equal(
            DayOfWeek.Saturday,
            window.DayOfWeek);

        Assert.Equal(
            new TimeOnly(9, 30),
            window.StartTime);

        Assert.Equal(
            new TimeOnly(13, 15),
            window.EndTime);

        Assert.False(window.EndsNextDay);

        Assert.Equal(
            TimeSpan.FromHours(3) +
            TimeSpan.FromMinutes(45),
            window.Duration);
    }

    [Fact]
    public void Create_ShouldAllowOvernightHourlyWindow()
    {
        CompanionAvailabilityWindow window =
            CompanionAvailabilityWindow.Create(
                CompanionBookingType.Hourly,
                DayOfWeek.Saturday,
                new TimeOnly(20, 0),
                new TimeOnly(1, 0));

        Assert.Equal(
            CompanionBookingType.Hourly,
            window.BookingType);

        Assert.True(window.EndsNextDay);

        Assert.Equal(
            TimeSpan.FromHours(5),
            window.Duration);
    }

    [Fact]
    public void Create_ShouldAllowExactEightHourDay()
    {
        CompanionAvailabilityWindow window =
            CompanionAvailabilityWindow.Create(
                CompanionBookingType.EightHourDay,
                DayOfWeek.Saturday,
                new TimeOnly(9, 0),
                new TimeOnly(17, 0));

        Assert.Equal(
            CompanionBookingType.EightHourDay,
            window.BookingType);

        Assert.Equal(
            TimeSpan.FromHours(8),
            window.Duration);

        Assert.False(window.EndsNextDay);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(9)]
    public void Create_ShouldRejectInvalidEightHourDuration(
        int durationHours)
    {
        TimeOnly startTime =
            new(8, 0);

        TimeOnly endTime =
            startTime.AddHours(
                durationHours);

        Assert.Throws<DomainException>(
            () => CompanionAvailabilityWindow.Create(
                CompanionBookingType.EightHourDay,
                DayOfWeek.Saturday,
                startTime,
                endTime));
    }

    [Fact]
    public void Create_ShouldRejectOvernightEightHourDay()
    {
        Assert.Throws<DomainException>(
            () => CompanionAvailabilityWindow.Create(
                CompanionBookingType.EightHourDay,
                DayOfWeek.Saturday,
                new TimeOnly(20, 0),
                new TimeOnly(4, 0)));
    }

    [Fact]
    public void Create_ShouldAllowFixedOvernightWindow()
    {
        CompanionAvailabilityWindow window =
            CompanionAvailabilityWindow.Create(
                CompanionBookingType.Overnight,
                DayOfWeek.Saturday,
                new TimeOnly(20, 0),
                new TimeOnly(8, 0));

        Assert.Equal(
            CompanionBookingType.Overnight,
            window.BookingType);

        Assert.True(window.EndsNextDay);

        Assert.Equal(
            TimeSpan.FromHours(12),
            window.Duration);
    }

    [Theory]
    [InlineData(19, 8)]
    [InlineData(20, 7)]
    [InlineData(21, 9)]
    [InlineData(8, 20)]
    public void Create_ShouldRejectInvalidOvernightTimes(
        int startHour,
        int endHour)
    {
        Assert.Throws<DomainException>(
            () => CompanionAvailabilityWindow.Create(
                CompanionBookingType.Overnight,
                DayOfWeek.Saturday,
                new TimeOnly(startHour, 0),
                new TimeOnly(endHour, 0)));
    }

    [Theory]
    [InlineData(CompanionBookingType.Hourly)]
    [InlineData(CompanionBookingType.EightHourDay)]
    [InlineData(CompanionBookingType.Overnight)]
    public void Create_ShouldRejectEqualStartAndEndTimes(
        CompanionBookingType bookingType)
    {
        Assert.Throws<DomainException>(
            () => CompanionAvailabilityWindow.Create(
                bookingType,
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(8, 0)));
    }

    [Fact]
    public void Create_ShouldRejectInvalidDayOfWeek()
    {
        Assert.Throws<DomainException>(
            () => CompanionAvailabilityWindow.Create(
                CompanionBookingType.Hourly,
                (DayOfWeek)999,
                new TimeOnly(8, 0),
                new TimeOnly(12, 0)));
    }

    [Fact]
    public void Create_ShouldRejectInvalidBookingType()
    {
        Assert.Throws<DomainException>(
            () => CompanionAvailabilityWindow.Create(
                (CompanionBookingType)999,
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(12, 0)));
    }

    [Fact]
    public void EqualWindows_ShouldHaveValueEquality()
    {
        CompanionAvailabilityWindow first =
            CompanionAvailabilityWindow.Create(
                CompanionBookingType.Hourly,
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(12, 0));

        CompanionAvailabilityWindow second =
            CompanionAvailabilityWindow.Create(
                CompanionBookingType.Hourly,
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(12, 0));

        Assert.Equal(first, second);
    }

    [Fact]
    public void WindowsWithDifferentTimes_ShouldNotBeEqual()
    {
        CompanionAvailabilityWindow first =
            CompanionAvailabilityWindow.Create(
                CompanionBookingType.Hourly,
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(12, 0));

        CompanionAvailabilityWindow second =
            CompanionAvailabilityWindow.Create(
                CompanionBookingType.Hourly,
                DayOfWeek.Saturday,
                new TimeOnly(9, 0),
                new TimeOnly(13, 0));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void WindowsWithSameTimesButDifferentProducts_ShouldNotBeEqual()
    {
        CompanionAvailabilityWindow hourly =
            CompanionAvailabilityWindow.Create(
                CompanionBookingType.Hourly,
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(16, 0));

        CompanionAvailabilityWindow day =
            CompanionAvailabilityWindow.Create(
                CompanionBookingType.EightHourDay,
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(16, 0));

        Assert.NotEqual(hourly, day);
    }
}