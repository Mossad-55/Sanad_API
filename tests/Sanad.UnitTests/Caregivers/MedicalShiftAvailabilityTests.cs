using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class MedicalShiftAvailabilityTests
{
    [Theory]
    [InlineData(
        MedicalShiftType.EightHourMorning,
        8,
        16,
        8,
        false)]
    [InlineData(
        MedicalShiftType.EightHourEvening,
        16,
        0,
        8,
        true)]
    [InlineData(
        MedicalShiftType.EightHourNight,
        0,
        8,
        8,
        false)]
    [InlineData(
        MedicalShiftType.TwelveHourDay,
        8,
        20,
        12,
        false)]
    [InlineData(
        MedicalShiftType.TwelveHourNight,
        20,
        8,
        12,
        true)]
    [InlineData(
        MedicalShiftType.TwentyFourHourLiveIn,
        8,
        8,
        24,
        true)]
    public void Create_ShouldUseFixedShiftTemplate(
        MedicalShiftType shiftType,
        int expectedStartHour,
        int expectedEndHour,
        int expectedDurationHours,
        bool expectedEndsNextDay)
    {
        MedicalShiftAvailability availability =
            MedicalShiftAvailability.Create(
                DayOfWeek.Saturday,
                shiftType);

        Assert.Equal(
            DayOfWeek.Saturday,
            availability.DayOfWeek);

        Assert.Equal(
            shiftType,
            availability.ShiftType);

        Assert.Equal(
            new TimeOnly(
                expectedStartHour,
                0),
            availability.StartTime);

        Assert.Equal(
            new TimeOnly(
                expectedEndHour,
                0),
            availability.EndTime);

        Assert.Equal(
            TimeSpan.FromHours(
                expectedDurationHours),
            availability.Duration);

        Assert.Equal(
            expectedEndsNextDay,
            availability.EndsNextDay);
    }

    [Fact]
    public void Create_ShouldRejectInvalidDayOfWeek()
    {
        Assert.Throws<DomainException>(
            () => MedicalShiftAvailability.Create(
                (DayOfWeek)999,
                MedicalShiftType.EightHourMorning));
    }

    [Fact]
    public void Create_ShouldRejectInvalidShiftType()
    {
        Assert.Throws<DomainException>(
            () => MedicalShiftAvailability.Create(
                DayOfWeek.Saturday,
                (MedicalShiftType)999));
    }

    [Fact]
    public void EqualShifts_ShouldHaveValueEquality()
    {
        MedicalShiftAvailability first =
            MedicalShiftAvailability.Create(
                DayOfWeek.Saturday,
                MedicalShiftType.TwelveHourNight);

        MedicalShiftAvailability second =
            MedicalShiftAvailability.Create(
                DayOfWeek.Saturday,
                MedicalShiftType.TwelveHourNight);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentTemplates_ShouldNotBeEqual()
    {
        MedicalShiftAvailability day =
            MedicalShiftAvailability.Create(
                DayOfWeek.Saturday,
                MedicalShiftType.TwelveHourDay);

        MedicalShiftAvailability night =
            MedicalShiftAvailability.Create(
                DayOfWeek.Saturday,
                MedicalShiftType.TwelveHourNight);

        Assert.NotEqual(day, night);
    }
}