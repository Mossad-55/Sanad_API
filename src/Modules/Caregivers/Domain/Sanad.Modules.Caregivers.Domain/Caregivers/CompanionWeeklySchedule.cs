using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class CompanionWeeklySchedule :
    ValueObject
{
    private const long WeekTicks =
        TimeSpan.TicksPerDay * 7;

    private readonly List<CompanionAvailabilityWindow>
        _windows = [];

    private CompanionWeeklySchedule()
    {
    }

    private CompanionWeeklySchedule(
        IEnumerable<CompanionAvailabilityWindow> windows)
    {
        _windows =
        [
            .. windows
                .OrderBy(
                    window =>
                        (int)window.DayOfWeek)
                .ThenBy(
                    window =>
                        window.StartTime)
        ];
    }

    public IReadOnlyCollection<CompanionAvailabilityWindow>
        Windows =>
            _windows.AsReadOnly();

    public bool HasAvailability =>
        _windows.Count > 0;

    internal static CompanionWeeklySchedule Create()
    {
        return new CompanionWeeklySchedule();
    }

    internal CompanionWeeklySchedule AddWindow(
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        CompanionAvailabilityWindow candidate =
            CompanionAvailabilityWindow.Create(
                dayOfWeek,
                startTime,
                endTime);

        bool overlapsExistingWindow =
            _windows.Any(
                existingWindow =>
                    Overlaps(
                        candidate,
                        existingWindow));

        if (overlapsExistingWindow)
        {
            throw new DomainException(
                "Availability window overlaps " +
                "an existing window.");
        }

        return new CompanionWeeklySchedule(
        [
            .. _windows,
            candidate
        ]);
    }

    internal CompanionWeeklySchedule RemoveWindow(
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        CompanionAvailabilityWindow target =
            CompanionAvailabilityWindow.Create(
                dayOfWeek,
                startTime,
                endTime);

        CompanionAvailabilityWindow? existing =
            _windows.FirstOrDefault(
                window =>
                    window == target);

        if (existing is null)
        {
            throw new DomainException(
                "Availability window was not found.");
        }

        List<CompanionAvailabilityWindow>
            remainingWindows =
            [.. _windows];

        remainingWindows.Remove(existing);

        return new CompanionWeeklySchedule(
            remainingWindows);
    }

    protected override IEnumerable<object?>
        GetEqualityComponents()
    {
        foreach (
            CompanionAvailabilityWindow window
            in _windows)
        {
            yield return window;
        }
    }

    private static bool Overlaps(
        CompanionAvailabilityWindow first,
        CompanionAvailabilityWindow second)
    {
        long firstStart =
            GetWeeklyStartTicks(first);

        long firstEnd =
            firstStart +
            first.Duration.Ticks;

        long secondStart =
            GetWeeklyStartTicks(second);

        long secondEnd =
            secondStart +
            second.Duration.Ticks;

        for (int weekOffset = -1;
             weekOffset <= 1;
             weekOffset++)
        {
            long shiftedSecondStart =
                secondStart +
                weekOffset * WeekTicks;

            long shiftedSecondEnd =
                secondEnd +
                weekOffset * WeekTicks;

            bool overlaps =
                firstStart < shiftedSecondEnd &&
                shiftedSecondStart < firstEnd;

            if (overlaps)
            {
                return true;
            }
        }

        return false;
    }

    private static long GetWeeklyStartTicks(
        CompanionAvailabilityWindow window)
    {
        return
            (int)window.DayOfWeek *
            TimeSpan.TicksPerDay +
            window.StartTime
                .ToTimeSpan()
                .Ticks;
    }
}