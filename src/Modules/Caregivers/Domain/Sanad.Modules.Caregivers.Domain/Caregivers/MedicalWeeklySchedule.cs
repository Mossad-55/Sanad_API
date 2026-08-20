using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class MedicalWeeklySchedule :
    ValueObject
{
    private const long WeekTicks =
        TimeSpan.TicksPerDay * 7;

    private readonly List<MedicalShiftAvailability>
        _shifts = [];

    private readonly List<MedicalHomeVisitWindow>
        _homeVisitWindows = [];

    private MedicalWeeklySchedule()
    {
    }

    private MedicalWeeklySchedule(
        IEnumerable<MedicalShiftAvailability> shifts,
        IEnumerable<MedicalHomeVisitWindow>
            homeVisitWindows)
    {
        _shifts =
        [
            .. shifts
                .OrderBy(
                    shift =>
                        (int)shift.DayOfWeek)
                .ThenBy(
                    shift =>
                        shift.StartTime)
                .ThenBy(
                    shift =>
                        shift.ShiftType)
        ];

        _homeVisitWindows =
        [
            .. homeVisitWindows
                .OrderBy(
                    window =>
                        (int)window.DayOfWeek)
                .ThenBy(
                    window =>
                        window.StartTime)
                .ThenBy(
                    window =>
                        window.EndTime)
        ];
    }

    public IReadOnlyCollection<MedicalShiftAvailability>
        Shifts =>
            _shifts.AsReadOnly();

    public IReadOnlyCollection<MedicalHomeVisitWindow>
        HomeVisitWindows =>
            _homeVisitWindows.AsReadOnly();

    public bool HasAvailability =>
        _shifts.Count > 0 ||
        _homeVisitWindows.Count > 0;

    internal static MedicalWeeklySchedule Create()
    {
        return new MedicalWeeklySchedule();
    }

    internal MedicalWeeklySchedule AddShift(
        DayOfWeek dayOfWeek,
        MedicalShiftType shiftType)
    {
        MedicalShiftAvailability candidate =
            MedicalShiftAvailability.Create(
                dayOfWeek,
                shiftType);

        bool alreadyHasShiftOnDay =
            _shifts.Any(
                shift =>
                    shift.DayOfWeek ==
                    dayOfWeek);

        if (alreadyHasShiftOnDay)
        {
            throw new DomainException(
                "A Medical shift is already configured " +
                "for this day.");
        }

        bool hasHomeVisitModeOnDay =
            _homeVisitWindows.Any(
                window =>
                    window.DayOfWeek ==
                    dayOfWeek);

        if (hasHomeVisitModeOnDay)
        {
            throw new DomainException(
                "A day cannot combine a Medical shift " +
                "with Home Visit windows.");
        }

        if (OverlapsExistingAvailability(
            candidate.DayOfWeek,
            candidate.StartTime,
            candidate.Duration))
        {
            throw new DomainException(
                "Medical shift overlaps existing " +
                "weekly availability.");
        }

        return new MedicalWeeklySchedule(
        [
            .. _shifts,
            candidate
        ],
        _homeVisitWindows);
    }

    internal MedicalWeeklySchedule RemoveShift(
        DayOfWeek dayOfWeek,
        MedicalShiftType shiftType)
    {
        MedicalShiftAvailability target =
            MedicalShiftAvailability.Create(
                dayOfWeek,
                shiftType);

        MedicalShiftAvailability? existing =
            _shifts.FirstOrDefault(
                shift =>
                    shift == target);

        if (existing is null)
        {
            throw new DomainException(
                "Medical shift was not found.");
        }

        List<MedicalShiftAvailability>
            remainingShifts =
            [.. _shifts];

        remainingShifts.Remove(existing);

        return new MedicalWeeklySchedule(
            remainingShifts,
            _homeVisitWindows);
    }

    internal MedicalWeeklySchedule AddHomeVisitWindow(
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        bool hasShiftModeOnDay =
            _shifts.Any(
                shift =>
                    shift.DayOfWeek ==
                    dayOfWeek);

        if (hasShiftModeOnDay)
        {
            throw new DomainException(
                "A day cannot combine Home Visit windows " +
                "with a Medical shift.");
        }

        MedicalHomeVisitWindow candidate =
            MedicalHomeVisitWindow.Create(
                dayOfWeek,
                startTime,
                endTime);

        if (OverlapsExistingAvailability(
            candidate.DayOfWeek,
            candidate.StartTime,
            candidate.Duration))
        {
            throw new DomainException(
                "Home Visit window overlaps existing " +
                "weekly availability.");
        }

        return new MedicalWeeklySchedule(
            _shifts,
        [
            .. _homeVisitWindows,
            candidate
        ]);
    }

    internal MedicalWeeklySchedule RemoveHomeVisitWindow(
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        MedicalHomeVisitWindow target =
            MedicalHomeVisitWindow.Create(
                dayOfWeek,
                startTime,
                endTime);

        MedicalHomeVisitWindow? existing =
            _homeVisitWindows.FirstOrDefault(
                window =>
                    window == target);

        if (existing is null)
        {
            throw new DomainException(
                "Home Visit window was not found.");
        }

        List<MedicalHomeVisitWindow>
            remainingWindows =
            [.. _homeVisitWindows];

        remainingWindows.Remove(existing);

        return new MedicalWeeklySchedule(
            _shifts,
            remainingWindows);
    }

    protected override IEnumerable<object?>
        GetEqualityComponents()
    {
        yield return _shifts.Count;

        foreach (
            MedicalShiftAvailability shift
            in _shifts)
        {
            yield return shift;
        }

        yield return _homeVisitWindows.Count;

        foreach (
            MedicalHomeVisitWindow window
            in _homeVisitWindows)
        {
            yield return window;
        }
    }

    private bool OverlapsExistingAvailability(
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeSpan duration)
    {
        bool overlapsShift =
            _shifts.Any(
                shift =>
                    Overlaps(
                        dayOfWeek,
                        startTime,
                        duration,
                        shift.DayOfWeek,
                        shift.StartTime,
                        shift.Duration));

        if (overlapsShift)
        {
            return true;
        }

        return _homeVisitWindows.Any(
            window =>
                Overlaps(
                    dayOfWeek,
                    startTime,
                    duration,
                    window.DayOfWeek,
                    window.StartTime,
                    window.Duration));
    }

    private static bool Overlaps(
        DayOfWeek firstDay,
        TimeOnly firstStartTime,
        TimeSpan firstDuration,
        DayOfWeek secondDay,
        TimeOnly secondStartTime,
        TimeSpan secondDuration)
    {
        long firstStart =
            GetWeeklyStartTicks(
                firstDay,
                firstStartTime);

        long firstEnd =
            firstStart +
            firstDuration.Ticks;

        long secondStart =
            GetWeeklyStartTicks(
                secondDay,
                secondStartTime);

        long secondEnd =
            secondStart +
            secondDuration.Ticks;

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
        DayOfWeek dayOfWeek,
        TimeOnly startTime)
    {
        return
            (int)dayOfWeek *
            TimeSpan.TicksPerDay +
            startTime
                .ToTimeSpan()
                .Ticks;
    }
}