using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.Availability;

/// <summary>
/// The single date-specific availability rule set. Both the
/// <c>availability/day</c> read and the checkout containment check call this
/// class, so the advisory read and the write gate cannot drift apart.
/// <para>
/// Declared windows are the caregiver's stored weekly schedule mapped by the
/// wire <see cref="BookingShiftType"/>: medical 8/12/24-hour shifts by stored
/// duration, medical home-visit windows, and companion
/// <c>EightHourDay</c>/<c>Overnight</c>/<c>Hourly</c> windows. Busy windows
/// reuse <b>exactly</b> the checkout overlap predicate
/// (<c>start &lt; busy.EndTime &amp;&amp; end &gt; busy.StartTime</c> over the
/// raw stored day times), which means the existing cross-midnight gap of that
/// predicate is mirrored here rather than silently fixed.
/// </para>
/// <para>
/// Fixed-duration shifts (8/12/24-hour) drop the whole declared interval when
/// any busy booking overlaps it; hourly and home-visit windows subtract only
/// the overlapping portion. No hold or expiry concept is invented: caregiver
/// bookings carry <c>AcceptanceDeadlineUtc</c>, an acceptance deadline, not an
/// availability hold.
/// </para>
/// </summary>
public static class CaregiverDayAvailabilityRules
{
    private const long TicksPerDay = TimeSpan.TicksPerDay;

    public static IReadOnlyList<CaregiverDayAvailabilityInterval> Build(
        Caregiver caregiver,
        DateOnly date,
        BookingShiftType shiftType,
        IReadOnlyList<CaregiverBusyWindow> busy)
    {
        if (caregiver.Status != CaregiverStatus.Active ||
            caregiver.Availability != CaregiverAvailability.Available)
        {
            // An inactive or manually unavailable caregiver answers 200 with
            // no intervals. No date-specific leave entity is invented.
            return [];
        }

        TimeZoneInfo zone = CaregiverAvailabilityTimeZone.Zone;
        bool subtractPortion = shiftType is
            BookingShiftType.Hourly or
            BookingShiftType.HomeVisit;

        var intervals = new List<CaregiverDayAvailabilityInterval>();

        foreach (DeclaredWindow window in DeclaredWindows(caregiver, date, shiftType))
        {
            long windowStart = window.StartTime.ToTimeSpan().Ticks;
            long windowDuration = window.Duration.Ticks;

            if (windowDuration <= 0)
            {
                continue;
            }

            long windowEnd = windowStart + windowDuration;
            TimeOnly windowEndTime = new(
                ((windowStart + windowDuration) % TicksPerDay + TicksPerDay) % TicksPerDay);

            List<CaregiverBusyWindow> overlapping = busy
                .Where(item =>
                    window.StartTime < item.EndTime &&
                    windowEndTime > item.StartTime)
                .ToList();

            if (overlapping.Count > 0 && !subtractPortion)
            {
                // A fixed-duration shift is offered whole or not at all.
                continue;
            }

            foreach ((long segmentStart, long segmentEnd) in
                Subtract(windowStart, windowEnd, overlapping))
            {
                if (!TryResolveInstant(date, segmentStart, zone, out DateTimeOffset startsAt) ||
                    !TryResolveInstant(date, segmentEnd, zone, out DateTimeOffset endsAt))
                {
                    // A boundary that falls in a spring-forward gap does not
                    // exist; omit the interval instead of inventing an offset.
                    continue;
                }

                if (endsAt <= startsAt)
                {
                    continue;
                }

                intervals.Add(new CaregiverDayAvailabilityInterval(startsAt, endsAt));
            }
        }

        return intervals
            .OrderBy(interval => interval.StartsAt)
            .ThenBy(interval => interval.EndsAt)
            .ToArray();
    }

    /// <summary>
    /// Exact containment of <c>[startTime, endTime)</c> in one returned
    /// interval. The requested range is resolved the same way the intervals
    /// are, so an unavailable caregiver, a busy slot, a window boundary that
    /// does not exist, or a range spanning two intervals all answer false.
    /// </summary>
    public static bool Contains(
        Caregiver caregiver,
        DateOnly date,
        BookingShiftType shiftType,
        IReadOnlyList<CaregiverBusyWindow> busy,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        IReadOnlyList<CaregiverDayAvailabilityInterval> intervals =
            Build(caregiver, date, shiftType, busy);

        if (intervals.Count == 0)
        {
            return false;
        }

        TimeZoneInfo zone = CaregiverAvailabilityTimeZone.Zone;
        long startTicks = startTime.ToTimeSpan().Ticks;
        long durationTicks = DurationTicks(startTime, endTime);

        if (durationTicks <= 0 ||
            !TryResolveInstant(date, startTicks, zone, out DateTimeOffset requestedStart) ||
            !TryResolveInstant(date, startTicks + durationTicks, zone, out DateTimeOffset requestedEnd))
        {
            return false;
        }

        return intervals.Any(interval =>
            interval.StartsAt <= requestedStart &&
            requestedEnd <= interval.EndsAt);
    }

    private static IReadOnlyList<DeclaredWindow> DeclaredWindows(
        Caregiver caregiver,
        DateOnly date,
        BookingShiftType shiftType)
    {
        DayOfWeek dayOfWeek = date.DayOfWeek;
        var windows = new List<DeclaredWindow>();

        if (caregiver.MedicalSchedule is not null)
        {
            if (shiftType == BookingShiftType.HomeVisit)
            {
                windows.AddRange(
                    caregiver.MedicalSchedule.HomeVisitWindows
                        .Where(window => window.DayOfWeek == dayOfWeek)
                        .Select(window => new DeclaredWindow(
                            window.StartTime,
                            window.Duration)));
            }
            else if (MedicalShiftDuration(shiftType) is { } duration)
            {
                windows.AddRange(
                    caregiver.MedicalSchedule.Shifts
                        .Where(shift =>
                            shift.DayOfWeek == dayOfWeek &&
                            shift.Duration == duration)
                        .Select(shift => new DeclaredWindow(
                            shift.StartTime,
                            duration)));
            }
        }

        if (caregiver.CompanionSchedule is not null &&
            CompanionBookingTypeFor(shiftType) is { } bookingType)
        {
            windows.AddRange(
                caregiver.CompanionSchedule.Windows
                    .Where(window =>
                        window.DayOfWeek == dayOfWeek &&
                        window.BookingType == bookingType)
                    .Select(window => new DeclaredWindow(
                        window.StartTime,
                        window.Duration)));
        }

        return windows;
    }

    private static TimeSpan? MedicalShiftDuration(BookingShiftType shiftType) =>
        shiftType switch
        {
            BookingShiftType.EightHourShift => TimeSpan.FromHours(8),
            BookingShiftType.TwelveHourShift => TimeSpan.FromHours(12),
            BookingShiftType.TwentyFourHourShift => TimeSpan.FromHours(24),
            _ => null
        };

    private static CompanionBookingType? CompanionBookingTypeFor(
        BookingShiftType shiftType) =>
        shiftType switch
        {
            BookingShiftType.EightHourShift => CompanionBookingType.EightHourDay,
            // The 12-hour companion window is the fixed 20:00 -> 08:00
            // overnight window, not a generic 12-hour block.
            BookingShiftType.TwelveHourShift => CompanionBookingType.Overnight,
            BookingShiftType.Hourly => CompanionBookingType.Hourly,
            _ => null
        };

    /// <summary>
    /// Subtracts each busy occurrence from a declared window in local tick
    /// space. Busy windows are repeated at neighbouring day offsets so a
    /// booking that crosses midnight inside the window is still removed.
    /// </summary>
    private static IReadOnlyList<(long Start, long End)> Subtract(
        long windowStart,
        long windowEnd,
        IReadOnlyList<CaregiverBusyWindow> busy)
    {
        var segments = new List<(long Start, long End)> { (windowStart, windowEnd) };

        foreach (CaregiverBusyWindow item in busy)
        {
            long busyStart = item.StartTime.ToTimeSpan().Ticks;
            long busyDuration = DurationTicks(item.StartTime, item.EndTime);

            if (busyDuration <= 0)
            {
                continue;
            }

            for (int dayOffset = -1; dayOffset <= 1; dayOffset++)
            {
                long occurrenceStart = busyStart + (dayOffset * TicksPerDay);
                long occurrenceEnd = occurrenceStart + busyDuration;

                var remaining = new List<(long Start, long End)>();

                foreach ((long start, long end) in segments)
                {
                    if (occurrenceEnd <= start || end <= occurrenceStart)
                    {
                        remaining.Add((start, end));
                        continue;
                    }

                    if (start < occurrenceStart)
                    {
                        remaining.Add((start, occurrenceStart));
                    }

                    if (occurrenceEnd < end)
                    {
                        remaining.Add((occurrenceEnd, end));
                    }
                }

                segments = remaining;
            }
        }

        return segments;
    }

    private static bool TryResolveInstant(
        DateOnly date,
        long localTicks,
        TimeZoneInfo zone,
        out DateTimeOffset instant)
    {
        DateTime local = date
            .ToDateTime(TimeOnly.MinValue)
            .AddTicks(localTicks);

        if (zone.IsInvalidTime(local))
        {
            instant = default;
            return false;
        }

        // Ambiguous fall-back boundaries resolve deterministically to the
        // standard offset (the zone's base offset); a boundary in a
        // spring-forward gap was already omitted above.
        TimeSpan offset = zone.IsAmbiguousTime(local)
            ? zone.BaseUtcOffset
            : zone.GetUtcOffset(local);

        instant = new DateTimeOffset(local, offset).ToUniversalTime();
        return true;
    }

    private static long DurationTicks(TimeOnly startTime, TimeOnly endTime)
    {
        long duration = endTime.ToTimeSpan().Ticks - startTime.ToTimeSpan().Ticks;

        if (duration > 0)
        {
            return duration;
        }

        return duration + TicksPerDay;
    }

    private readonly record struct DeclaredWindow(
        TimeOnly StartTime,
        TimeSpan Duration);
}
