using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class MedicalShiftAvailability :
    ValueObject
{
    private MedicalShiftAvailability()
    {
    }

    private MedicalShiftAvailability(
        DayOfWeek dayOfWeek,
        MedicalShiftType shiftType)
    {
        DayOfWeek = dayOfWeek;
        ShiftType = shiftType;
    }

    public DayOfWeek DayOfWeek { get; private set; }

    public MedicalShiftType ShiftType { get; private set; }

    public TimeOnly StartTime =>
        ShiftType switch
        {
            MedicalShiftType.EightHourMorning =>
                new TimeOnly(8, 0),

            MedicalShiftType.EightHourEvening =>
                new TimeOnly(16, 0),

            MedicalShiftType.EightHourNight =>
                new TimeOnly(0, 0),

            MedicalShiftType.TwelveHourDay =>
                new TimeOnly(8, 0),

            MedicalShiftType.TwelveHourNight =>
                new TimeOnly(20, 0),

            MedicalShiftType.TwentyFourHourLiveIn =>
                new TimeOnly(8, 0),

            _ => throw new DomainException(
                "Medical shift type is invalid.")
        };

    public TimeOnly EndTime =>
        ShiftType switch
        {
            MedicalShiftType.EightHourMorning =>
                new TimeOnly(16, 0),

            MedicalShiftType.EightHourEvening =>
                new TimeOnly(0, 0),

            MedicalShiftType.EightHourNight =>
                new TimeOnly(8, 0),

            MedicalShiftType.TwelveHourDay =>
                new TimeOnly(20, 0),

            MedicalShiftType.TwelveHourNight =>
                new TimeOnly(8, 0),

            MedicalShiftType.TwentyFourHourLiveIn =>
                new TimeOnly(8, 0),

            _ => throw new DomainException(
                "Medical shift type is invalid.")
        };

    public TimeSpan Duration =>
        ShiftType switch
        {
            MedicalShiftType.EightHourMorning or
            MedicalShiftType.EightHourEvening or
            MedicalShiftType.EightHourNight =>
                TimeSpan.FromHours(8),

            MedicalShiftType.TwelveHourDay or
            MedicalShiftType.TwelveHourNight =>
                TimeSpan.FromHours(12),

            MedicalShiftType.TwentyFourHourLiveIn =>
                TimeSpan.FromHours(24),

            _ => throw new DomainException(
                "Medical shift type is invalid.")
        };

    public bool EndsNextDay =>
        ShiftType is
            MedicalShiftType.EightHourEvening or
            MedicalShiftType.TwelveHourNight or
            MedicalShiftType.TwentyFourHourLiveIn;

    internal static MedicalShiftAvailability Create(
        DayOfWeek dayOfWeek,
        MedicalShiftType shiftType)
    {
        if (!Enum.IsDefined(dayOfWeek))
        {
            throw new DomainException(
                "Day of week is invalid.");
        }

        if (!Enum.IsDefined(shiftType))
        {
            throw new DomainException(
                "Medical shift type is invalid.");
        }

        return new MedicalShiftAvailability(
            dayOfWeek,
            shiftType);
    }

    protected override IEnumerable<object?>
        GetEqualityComponents()
    {
        yield return DayOfWeek;
        yield return ShiftType;
    }
}