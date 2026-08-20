using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class CompanionAvailabilityWindow :
    ValueObject
{
    private CompanionAvailabilityWindow()
    {
    }

    private CompanionAvailabilityWindow(
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

    public bool EndsNextDay =>
        EndTime < StartTime;

    public TimeSpan Duration
    {
        get
        {
            TimeSpan duration =
                EndTime.ToTimeSpan() -
                StartTime.ToTimeSpan();

            if (duration > TimeSpan.Zero)
            {
                return duration;
            }

            return duration +
                TimeSpan.FromDays(1);
        }
    }

    internal static CompanionAvailabilityWindow Create(
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        if (!Enum.IsDefined(dayOfWeek))
        {
            throw new DomainException(
                "Day of week is invalid.");
        }

        if (startTime == endTime)
        {
            throw new DomainException(
                "Availability start and end times " +
                "cannot be equal.");
        }

        return new CompanionAvailabilityWindow(
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