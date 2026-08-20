using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class MedicalHomeVisitWindow :
    ValueObject
{
    private MedicalHomeVisitWindow()
    {
    }

    private MedicalHomeVisitWindow(
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
    }

    public DayOfWeek DayOfWeek { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public TimeSpan Duration =>
        EndTime.ToTimeSpan() -
        StartTime.ToTimeSpan();

    internal static MedicalHomeVisitWindow Create(
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        if (!Enum.IsDefined(dayOfWeek))
        {
            throw new DomainException(
                "Day of week is invalid.");
        }

        if (endTime <= startTime)
        {
            throw new DomainException(
                "A Home Visit window must end " +
                "after it starts on the same day.");
        }

        return new MedicalHomeVisitWindow(
            dayOfWeek,
            startTime,
            endTime);
    }

    protected override IEnumerable<object?>
        GetEqualityComponents()
    {
        yield return DayOfWeek;
        yield return StartTime;
        yield return EndTime;
    }
}