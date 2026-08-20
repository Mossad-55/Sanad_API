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
        CompanionBookingType bookingType,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        BookingType = bookingType;
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
    }

    public CompanionBookingType BookingType
    {
        get;
        private set;
    }

    public DayOfWeek DayOfWeek { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public bool EndsNextDay =>
        EndTime < StartTime;

    public TimeSpan Duration =>
        CalculateDuration(
            StartTime,
            EndTime);

    internal static CompanionAvailabilityWindow Create(
        CompanionBookingType bookingType,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        if (!Enum.IsDefined(bookingType))
        {
            throw new DomainException(
                "Companion booking type is invalid.");
        }

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

        TimeSpan duration =
            CalculateDuration(
                startTime,
                endTime);

        ValidateBookingTypeWindow(
            bookingType,
            startTime,
            endTime,
            duration);

        return new CompanionAvailabilityWindow(
            bookingType,
            dayOfWeek,
            startTime,
            endTime);
    }

    protected override IEnumerable<object?>
        GetEqualityComponents()
    {
        yield return BookingType;
        yield return DayOfWeek;
        yield return StartTime;
        yield return EndTime;
    }

    private static void ValidateBookingTypeWindow(
        CompanionBookingType bookingType,
        TimeOnly startTime,
        TimeOnly endTime,
        TimeSpan duration)
    {
        switch (bookingType)
        {
            case CompanionBookingType.Hourly:
                return;

            case CompanionBookingType.EightHourDay:
                if (endTime < startTime)
                {
                    throw new DomainException(
                        "An 8-hour Day window cannot " +
                        "continue into the next day.");
                }

                if (duration !=
                    TimeSpan.FromHours(8))
                {
                    throw new DomainException(
                        "An 8-hour Day window must be " +
                        "exactly 8 hours.");
                }

                return;

            case CompanionBookingType.Overnight:
                bool isFixedOvernightWindow =
                    startTime ==
                    new TimeOnly(20, 0) &&
                    endTime ==
                    new TimeOnly(8, 0);

                if (!isFixedOvernightWindow)
                {
                    throw new DomainException(
                        "An Overnight window must be " +
                        "20:00 to 08:00 next day.");
                }

                return;

            default:
                throw new DomainException(
                    "Companion booking type is invalid.");
        }
    }

    private static TimeSpan CalculateDuration(
        TimeOnly startTime,
        TimeOnly endTime)
    {
        TimeSpan duration =
            endTime.ToTimeSpan() -
            startTime.ToTimeSpan();

        if (duration > TimeSpan.Zero)
        {
            return duration;
        }

        return duration +
            TimeSpan.FromDays(1);
    }
}