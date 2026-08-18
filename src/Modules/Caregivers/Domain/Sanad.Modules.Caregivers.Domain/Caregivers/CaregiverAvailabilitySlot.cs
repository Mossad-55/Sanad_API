using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class CaregiverAvailabilitySlot : ValueObject
{
    private CaregiverAvailabilitySlot()
    {
    }

    private CaregiverAvailabilitySlot(
        DayOfWeek dayOfWeek,
        TimeOnly? startTime,
        TimeOnly? endTime,
        MedicalShiftType? medicalShiftType)
    {
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
        MedicalShiftType = medicalShiftType;
    }

    public DayOfWeek DayOfWeek { get; private set; }

    public TimeOnly? StartTime { get; private set; }

    public TimeOnly? EndTime { get; private set; }

    public MedicalShiftType? MedicalShiftType { get; private set; }

    public static CaregiverAvailabilitySlot CreateCompanion(
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        if (startTime == endTime)
        {
            throw new DomainException(
                "Start time and end time cannot be equal.");
        }

        return new CaregiverAvailabilitySlot(
            dayOfWeek,
            startTime,
            endTime,
            null);
    }

    public static CaregiverAvailabilitySlot CreateMedical(
        DayOfWeek dayOfWeek,
        MedicalShiftType shiftType)
    {
        return new CaregiverAvailabilitySlot(
            dayOfWeek,
            null,
            null,
            shiftType);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DayOfWeek;
        yield return StartTime;
        yield return EndTime;
        yield return MedicalShiftType;
    }
}