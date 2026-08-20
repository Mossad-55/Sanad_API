using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CompanionWeeklyScheduleTests
{
    [Fact]
    public void Create_ShouldCreateEmptySchedule()
    {
        CompanionWeeklySchedule schedule =
            CompanionWeeklySchedule.Create();

        Assert.Empty(schedule.Windows);
        Assert.False(schedule.HasAvailability);
    }

    [Fact]
    public void AddWindow_ShouldReturnNewSchedule()
    {
        CompanionWeeklySchedule original =
            CompanionWeeklySchedule.Create();

        CompanionWeeklySchedule updated =
            original.AddWindow(
                CompanionBookingType.Hourly,
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(12, 0));

        Assert.Empty(original.Windows);
        Assert.Single(updated.Windows);
        Assert.True(updated.HasAvailability);
    }

    [Fact]
    public void AddWindow_ShouldPreserveBookingType()
    {
        CompanionWeeklySchedule schedule =
            CompanionWeeklySchedule.Create()
                .AddWindow(
                    CompanionBookingType.EightHourDay,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(16, 0));

        CompanionAvailabilityWindow window =
            Assert.Single(
                schedule.Windows);

        Assert.Equal(
            CompanionBookingType.EightHourDay,
            window.BookingType);
    }

    [Fact]
    public void AddWindow_ShouldAllowMultipleNonOverlappingProducts()
    {
        CompanionWeeklySchedule schedule =
            CompanionWeeklySchedule.Create()
                .AddWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(12, 0))
                .AddWindow(
                    CompanionBookingType.Overnight,
                    DayOfWeek.Saturday,
                    new TimeOnly(20, 0),
                    new TimeOnly(8, 0));

        Assert.Equal(
            2,
            schedule.Windows.Count);
    }

    [Fact]
    public void AddWindow_ShouldAllowAdjacentWindows()
    {
        CompanionWeeklySchedule schedule =
            CompanionWeeklySchedule.Create()
                .AddWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(12, 0))
                .AddWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Saturday,
                    new TimeOnly(12, 0),
                    new TimeOnly(16, 0));

        Assert.Equal(
            2,
            schedule.Windows.Count);
    }

    [Fact]
    public void AddWindow_ShouldRejectSameProductOverlap()
    {
        CompanionWeeklySchedule schedule =
            CompanionWeeklySchedule.Create()
                .AddWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(14, 0));

        Assert.Throws<DomainException>(
            () => schedule.AddWindow(
                CompanionBookingType.Hourly,
                DayOfWeek.Saturday,
                new TimeOnly(12, 0),
                new TimeOnly(16, 0)));

        Assert.Single(schedule.Windows);
    }

    [Fact]
    public void AddWindow_ShouldRejectOverlapAcrossDifferentProducts()
    {
        CompanionWeeklySchedule schedule =
            CompanionWeeklySchedule.Create()
                .AddWindow(
                    CompanionBookingType.EightHourDay,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(16, 0));

        Assert.Throws<DomainException>(
            () => schedule.AddWindow(
                CompanionBookingType.Hourly,
                DayOfWeek.Saturday,
                new TimeOnly(12, 0),
                new TimeOnly(18, 0)));
    }

    [Fact]
    public void AddWindow_ShouldRejectDuplicateWindow()
    {
        CompanionWeeklySchedule schedule =
            CompanionWeeklySchedule.Create()
                .AddWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(12, 0));

        Assert.Throws<DomainException>(
            () => schedule.AddWindow(
                CompanionBookingType.Hourly,
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(12, 0)));
    }

    [Fact]
    public void AddWindow_ShouldRejectSameTimesWithDifferentProduct()
    {
        CompanionWeeklySchedule schedule =
            CompanionWeeklySchedule.Create()
                .AddWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(16, 0));

        Assert.Throws<DomainException>(
            () => schedule.AddWindow(
                CompanionBookingType.EightHourDay,
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(16, 0)));
    }

    [Fact]
    public void AddWindow_ShouldRejectOverlapWithPreviousOvernightWindow()
    {
        CompanionWeeklySchedule schedule =
            CompanionWeeklySchedule.Create()
                .AddWindow(
                    CompanionBookingType.Overnight,
                    DayOfWeek.Saturday,
                    new TimeOnly(20, 0),
                    new TimeOnly(8, 0));

        Assert.Throws<DomainException>(
            () => schedule.AddWindow(
                CompanionBookingType.Hourly,
                DayOfWeek.Sunday,
                new TimeOnly(7, 0),
                new TimeOnly(10, 0)));
    }

    [Fact]
    public void AddWindow_ShouldAllowWindowAdjacentToOvernightEnd()
    {
        CompanionWeeklySchedule schedule =
            CompanionWeeklySchedule.Create()
                .AddWindow(
                    CompanionBookingType.Overnight,
                    DayOfWeek.Saturday,
                    new TimeOnly(20, 0),
                    new TimeOnly(8, 0))
                .AddWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Sunday,
                    new TimeOnly(8, 0),
                    new TimeOnly(12, 0));

        Assert.Equal(
            2,
            schedule.Windows.Count);
    }

    [Fact]
    public void RemoveWindow_ShouldReturnNewSchedule()
    {
        CompanionWeeklySchedule original =
            CompanionWeeklySchedule.Create()
                .AddWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(12, 0));

        CompanionWeeklySchedule updated =
            original.RemoveWindow(
                CompanionBookingType.Hourly,
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(12, 0));

        Assert.Single(original.Windows);
        Assert.Empty(updated.Windows);
        Assert.False(updated.HasAvailability);
    }

    [Fact]
    public void RemoveWindow_ShouldRequireMatchingBookingType()
    {
        CompanionWeeklySchedule schedule =
            CompanionWeeklySchedule.Create()
                .AddWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(16, 0));

        Assert.Throws<DomainException>(
            () => schedule.RemoveWindow(
                CompanionBookingType.EightHourDay,
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(16, 0)));

        Assert.Single(schedule.Windows);
    }

    [Fact]
    public void RemoveWindow_ShouldRejectMissingWindow()
    {
        CompanionWeeklySchedule schedule =
            CompanionWeeklySchedule.Create();

        Assert.Throws<DomainException>(
            () => schedule.RemoveWindow(
                CompanionBookingType.Hourly,
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(12, 0)));
    }

    [Fact]
    public void Schedules_ShouldBeEqualRegardlessOfAddOrder()
    {
        CompanionWeeklySchedule first =
            CompanionWeeklySchedule.Create()
                .AddWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Sunday,
                    new TimeOnly(14, 0),
                    new TimeOnly(18, 0))
                .AddWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(12, 0));

        CompanionWeeklySchedule second =
            CompanionWeeklySchedule.Create()
                .AddWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(12, 0))
                .AddWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Sunday,
                    new TimeOnly(14, 0),
                    new TimeOnly(18, 0));

        Assert.Equal(first, second);
    }

    [Fact]
    public void SchedulesWithDifferentProducts_ShouldNotBeEqual()
    {
        CompanionWeeklySchedule hourly =
            CompanionWeeklySchedule.Create()
                .AddWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(16, 0));

        CompanionWeeklySchedule day =
            CompanionWeeklySchedule.Create()
                .AddWindow(
                    CompanionBookingType.EightHourDay,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(16, 0));

        Assert.NotEqual(hourly, day);
    }
}