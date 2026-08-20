using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class MedicalWeeklyScheduleTests
{
    [Fact]
    public void Create_ShouldCreateEmptySchedule()
    {
        MedicalWeeklySchedule schedule =
            MedicalWeeklySchedule.Create();

        Assert.Empty(schedule.Shifts);
        Assert.Empty(schedule.HomeVisitWindows);
        Assert.False(schedule.HasAvailability);
    }

    [Fact]
    public void AddShift_ShouldReturnNewSchedule()
    {
        MedicalWeeklySchedule original =
            MedicalWeeklySchedule.Create();

        MedicalWeeklySchedule updated =
            original.AddShift(
                DayOfWeek.Saturday,
                MedicalShiftType.EightHourMorning);

        Assert.Empty(original.Shifts);
        Assert.Single(updated.Shifts);
        Assert.True(updated.HasAvailability);
    }

    [Fact]
    public void AddShift_ShouldPreserveShiftTemplate()
    {
        MedicalWeeklySchedule schedule =
            MedicalWeeklySchedule.Create()
                .AddShift(
                    DayOfWeek.Saturday,
                    MedicalShiftType.TwelveHourNight);

        MedicalShiftAvailability shift =
            Assert.Single(schedule.Shifts);

        Assert.Equal(
            DayOfWeek.Saturday,
            shift.DayOfWeek);

        Assert.Equal(
            MedicalShiftType.TwelveHourNight,
            shift.ShiftType);

        Assert.Equal(
            new TimeOnly(20, 0),
            shift.StartTime);

        Assert.Equal(
            new TimeOnly(8, 0),
            shift.EndTime);
    }

    [Fact]
    public void AddShift_ShouldRejectSecondShiftOnSameDay()
    {
        MedicalWeeklySchedule schedule =
            MedicalWeeklySchedule.Create()
                .AddShift(
                    DayOfWeek.Saturday,
                    MedicalShiftType.EightHourMorning);

        Assert.Throws<DomainException>(
            () => schedule.AddShift(
                DayOfWeek.Saturday,
                MedicalShiftType.TwelveHourNight));

        Assert.Single(schedule.Shifts);
    }

    [Fact]
    public void AddShift_ShouldRejectDayWithHomeVisitMode()
    {
        MedicalWeeklySchedule schedule =
            MedicalWeeklySchedule.Create()
                .AddHomeVisitWindow(
                    DayOfWeek.Saturday,
                    new TimeOnly(9, 0),
                    new TimeOnly(12, 0));

        Assert.Throws<DomainException>(
            () => schedule.AddShift(
                DayOfWeek.Saturday,
                MedicalShiftType.EightHourEvening));

        Assert.Empty(schedule.Shifts);
        Assert.Single(schedule.HomeVisitWindows);
    }

    [Fact]
    public void AddShift_ShouldRejectOverlapWithPreviousOvernightShift()
    {
        MedicalWeeklySchedule schedule =
            MedicalWeeklySchedule.Create()
                .AddShift(
                    DayOfWeek.Saturday,
                    MedicalShiftType.TwelveHourNight);

        Assert.Throws<DomainException>(
            () => schedule.AddShift(
                DayOfWeek.Sunday,
                MedicalShiftType.EightHourNight));

        Assert.Single(schedule.Shifts);
    }

    [Fact]
    public void AddShift_ShouldAllowAdjacentShiftAfterOvernight()
    {
        MedicalWeeklySchedule schedule =
            MedicalWeeklySchedule.Create()
                .AddShift(
                    DayOfWeek.Saturday,
                    MedicalShiftType.TwelveHourNight)
                .AddShift(
                    DayOfWeek.Sunday,
                    MedicalShiftType.EightHourMorning);

        Assert.Equal(
            2,
            schedule.Shifts.Count);
    }

    [Fact]
    public void AddHomeVisitWindow_ShouldReturnNewSchedule()
    {
        MedicalWeeklySchedule original =
            MedicalWeeklySchedule.Create();

        MedicalWeeklySchedule updated =
            original.AddHomeVisitWindow(
                DayOfWeek.Saturday,
                new TimeOnly(9, 0),
                new TimeOnly(12, 0));

        Assert.Empty(original.HomeVisitWindows);
        Assert.Single(updated.HomeVisitWindows);
        Assert.True(updated.HasAvailability);
    }

    [Fact]
    public void AddHomeVisitWindow_ShouldAllowMultipleNonOverlappingWindows()
    {
        MedicalWeeklySchedule schedule =
            MedicalWeeklySchedule.Create()
                .AddHomeVisitWindow(
                    DayOfWeek.Saturday,
                    new TimeOnly(9, 0),
                    new TimeOnly(12, 0))
                .AddHomeVisitWindow(
                    DayOfWeek.Saturday,
                    new TimeOnly(15, 0),
                    new TimeOnly(18, 0));

        Assert.Equal(
            2,
            schedule.HomeVisitWindows.Count);
    }

    [Fact]
    public void AddHomeVisitWindow_ShouldAllowAdjacentWindows()
    {
        MedicalWeeklySchedule schedule =
            MedicalWeeklySchedule.Create()
                .AddHomeVisitWindow(
                    DayOfWeek.Saturday,
                    new TimeOnly(9, 0),
                    new TimeOnly(12, 0))
                .AddHomeVisitWindow(
                    DayOfWeek.Saturday,
                    new TimeOnly(12, 0),
                    new TimeOnly(15, 0));

        Assert.Equal(
            2,
            schedule.HomeVisitWindows.Count);
    }

    [Fact]
    public void AddHomeVisitWindow_ShouldRejectOverlappingWindow()
    {
        MedicalWeeklySchedule schedule =
            MedicalWeeklySchedule.Create()
                .AddHomeVisitWindow(
                    DayOfWeek.Saturday,
                    new TimeOnly(9, 0),
                    new TimeOnly(13, 0));

        Assert.Throws<DomainException>(
            () => schedule.AddHomeVisitWindow(
                DayOfWeek.Saturday,
                new TimeOnly(12, 0),
                new TimeOnly(15, 0)));

        Assert.Single(schedule.HomeVisitWindows);
    }

    [Fact]
    public void AddHomeVisitWindow_ShouldRejectDuplicateWindow()
    {
        MedicalWeeklySchedule schedule =
            MedicalWeeklySchedule.Create()
                .AddHomeVisitWindow(
                    DayOfWeek.Saturday,
                    new TimeOnly(9, 0),
                    new TimeOnly(12, 0));

        Assert.Throws<DomainException>(
            () => schedule.AddHomeVisitWindow(
                DayOfWeek.Saturday,
                new TimeOnly(9, 0),
                new TimeOnly(12, 0)));
    }

    [Fact]
    public void AddHomeVisitWindow_ShouldRejectDayWithShiftMode()
    {
        MedicalWeeklySchedule schedule =
            MedicalWeeklySchedule.Create()
                .AddShift(
                    DayOfWeek.Saturday,
                    MedicalShiftType.EightHourMorning);

        Assert.Throws<DomainException>(
            () => schedule.AddHomeVisitWindow(
                DayOfWeek.Saturday,
                new TimeOnly(18, 0),
                new TimeOnly(20, 0)));

        Assert.Single(schedule.Shifts);
        Assert.Empty(schedule.HomeVisitWindows);
    }

    [Fact]
    public void AddHomeVisitWindow_ShouldRejectOverlapWithPreviousOvernightShift()
    {
        MedicalWeeklySchedule schedule =
            MedicalWeeklySchedule.Create()
                .AddShift(
                    DayOfWeek.Saturday,
                    MedicalShiftType.TwelveHourNight);

        Assert.Throws<DomainException>(
            () => schedule.AddHomeVisitWindow(
                DayOfWeek.Sunday,
                new TimeOnly(7, 0),
                new TimeOnly(10, 0)));
    }

    [Fact]
    public void AddHomeVisitWindow_ShouldAllowAdjacencyAfterOvernightShift()
    {
        MedicalWeeklySchedule schedule =
            MedicalWeeklySchedule.Create()
                .AddShift(
                    DayOfWeek.Saturday,
                    MedicalShiftType.TwelveHourNight)
                .AddHomeVisitWindow(
                    DayOfWeek.Sunday,
                    new TimeOnly(8, 0),
                    new TimeOnly(10, 0));

        Assert.Single(schedule.Shifts);
        Assert.Single(schedule.HomeVisitWindows);
    }

    [Fact]
    public void RemoveShift_ShouldReturnNewSchedule()
    {
        MedicalWeeklySchedule original =
            MedicalWeeklySchedule.Create()
                .AddShift(
                    DayOfWeek.Saturday,
                    MedicalShiftType.EightHourMorning);

        MedicalWeeklySchedule updated =
            original.RemoveShift(
                DayOfWeek.Saturday,
                MedicalShiftType.EightHourMorning);

        Assert.Single(original.Shifts);
        Assert.Empty(updated.Shifts);
        Assert.False(updated.HasAvailability);
    }

    [Fact]
    public void RemoveShift_ShouldRequireMatchingTemplate()
    {
        MedicalWeeklySchedule schedule =
            MedicalWeeklySchedule.Create()
                .AddShift(
                    DayOfWeek.Saturday,
                    MedicalShiftType.EightHourMorning);

        Assert.Throws<DomainException>(
            () => schedule.RemoveShift(
                DayOfWeek.Saturday,
                MedicalShiftType.EightHourEvening));

        Assert.Single(schedule.Shifts);
    }

    [Fact]
    public void RemoveShift_ShouldRejectMissingShift()
    {
        MedicalWeeklySchedule schedule =
            MedicalWeeklySchedule.Create();

        Assert.Throws<DomainException>(
            () => schedule.RemoveShift(
                DayOfWeek.Saturday,
                MedicalShiftType.EightHourMorning));
    }

    [Fact]
    public void RemoveHomeVisitWindow_ShouldReturnNewSchedule()
    {
        MedicalWeeklySchedule original =
            MedicalWeeklySchedule.Create()
                .AddHomeVisitWindow(
                    DayOfWeek.Saturday,
                    new TimeOnly(9, 0),
                    new TimeOnly(12, 0));

        MedicalWeeklySchedule updated =
            original.RemoveHomeVisitWindow(
                DayOfWeek.Saturday,
                new TimeOnly(9, 0),
                new TimeOnly(12, 0));

        Assert.Single(original.HomeVisitWindows);
        Assert.Empty(updated.HomeVisitWindows);
        Assert.False(updated.HasAvailability);
    }

    [Fact]
    public void RemoveHomeVisitWindow_ShouldRejectMissingWindow()
    {
        MedicalWeeklySchedule schedule =
            MedicalWeeklySchedule.Create();

        Assert.Throws<DomainException>(
            () => schedule.RemoveHomeVisitWindow(
                DayOfWeek.Saturday,
                new TimeOnly(9, 0),
                new TimeOnly(12, 0)));
    }

    [Fact]
    public void Schedules_ShouldBeEqualRegardlessOfAddOrder()
    {
        MedicalWeeklySchedule first =
            MedicalWeeklySchedule.Create()
                .AddShift(
                    DayOfWeek.Sunday,
                    MedicalShiftType.EightHourMorning)
                .AddHomeVisitWindow(
                    DayOfWeek.Saturday,
                    new TimeOnly(9, 0),
                    new TimeOnly(12, 0));

        MedicalWeeklySchedule second =
            MedicalWeeklySchedule.Create()
                .AddHomeVisitWindow(
                    DayOfWeek.Saturday,
                    new TimeOnly(9, 0),
                    new TimeOnly(12, 0))
                .AddShift(
                    DayOfWeek.Sunday,
                    MedicalShiftType.EightHourMorning);

        Assert.Equal(first, second);
    }
}