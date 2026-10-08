using Sanad.Modules.CareHomes.Domain.Bookings;

namespace Sanad.Modules.CareHomes.Application.Visits;

public sealed record CareHomeVisitSettingsResponse(Guid FacilityId, int Version, int VisitorCapacity,
    IReadOnlyList<CareHomeVisitTimeWindow> OperatingHours,
    IReadOnlyList<CareHomeVisitTimeWindow> VisitingWindows,
    IReadOnlyList<CareHomeVisitClosure> Closures);

public sealed record CareHomeVisitSlot(DateTimeOffset StartsAt, DateTimeOffset EndsAt, int RemainingVisitorCapacity);
public sealed record CareHomeVisitAvailabilityResponse(Guid FacilityId, DateOnly Date, string TimeZoneId,
    int RequestedVisitorCount, int? VisitorCapacity, IReadOnlyList<CareHomeVisitSlot> Slots);

public sealed record CareHomeVisitResponse(Guid Id, Guid FacilityId, Guid FamilyId, CareHomeVisitKind Kind,
    Guid? ElderlyId, string VisitorName, string VisitorPhone, int VisitorCount,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, CareHomeVisitStatus Status,
    Guid? RescheduleOfVisitId, DateTimeOffset DecisionExpiresAt,
    Guid? DecidedByUserId, DateTimeOffset? DecidedAt, string? DecisionReason,
    Guid? CancelledByUserId, DateTimeOffset? CancelledAt, string? CancellationReason,
    DateTimeOffset CreatedAt);

public static class CareHomeVisitScheduleRules
{
    public const string CairoTimeZoneId = "Africa/Cairo";
    public static TimeZoneInfo CairoTimeZone { get; } = ResolveCairoTimeZone();

    public static IReadOnlyList<CareHomeVisitSlot> GetSlots(
        CareHomeVisitSettings settings, DateOnly date, int requestedVisitorCount,
        IReadOnlyList<CareHomeVisit> reservations, DateTime utcNow)
    {
        if (requestedVisitorCount < 1 || requestedVisitorCount > settings.VisitorCapacity || settings.IsClosed(date)) return [];
        DateTime minimumUtc = utcNow.AddHours(24);
        List<CareHomeVisitSlot> slots = [];
        foreach (CareHomeVisitTimeWindow window in settings.VisitingWindows.Where(x => x.Day == date.DayOfWeek).OrderBy(x => x.StartTime))
        {
            long startTicks = window.StartTime.Ticks;
            while (startTicks + TimeSpan.TicksPerHour <= window.EndTime.Ticks)
            {
                TimeOnly start = TimeOnly.FromTimeSpan(TimeSpan.FromTicks(startTicks));
                TimeOnly end = TimeOnly.FromTimeSpan(TimeSpan.FromTicks(startTicks + TimeSpan.TicksPerHour));
                if (settings.Allows(date, start, end))
                {
                    DateTime localStart = DateTime.SpecifyKind(date.ToDateTime(start), DateTimeKind.Unspecified);
                    DateTime localEnd = DateTime.SpecifyKind(date.ToDateTime(end), DateTimeKind.Unspecified);
                    if (!CairoTimeZone.IsInvalidTime(localStart) && !CairoTimeZone.IsAmbiguousTime(localStart)
                        && !CairoTimeZone.IsInvalidTime(localEnd) && !CairoTimeZone.IsAmbiguousTime(localEnd))
                    {
                        DateTime startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, CairoTimeZone);
                        DateTime endUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, CairoTimeZone);
                        if (startUtc >= minimumUtc)
                        {
                            int remaining = Math.Max(0, settings.VisitorCapacity - ReservedVisitorCount(reservations, startUtc, endUtc, utcNow));
                            if (remaining >= requestedVisitorCount)
                                slots.Add(new(new DateTimeOffset(startUtc, TimeSpan.Zero),
                                    new DateTimeOffset(endUtc, TimeSpan.Zero), remaining));
                        }
                    }
                }
                startTicks += TimeSpan.TicksPerHour;
            }
        }
        return slots;
    }

    public static bool IsAllowedStart(CareHomeVisitSettings settings, DateTime startsAtUtc)
    {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startsAtUtc, DateTimeKind.Utc), CairoTimeZone);
        DateOnly date = DateOnly.FromDateTime(local);
        TimeOnly start = TimeOnly.FromDateTime(local);
        if (CairoTimeZone.IsInvalidTime(local) || CairoTimeZone.IsAmbiguousTime(local)) return false;
        TimeOnly end;
        try { end = start.AddHours(1); }
        catch (ArgumentOutOfRangeException) { return false; }
        if (!settings.Allows(date, start, end)) return false;
        return settings.VisitingWindows.Any(window => window.Day == date.DayOfWeek
            && start >= window.StartTime && end <= window.EndTime
            && (start - window.StartTime).Ticks % TimeSpan.TicksPerHour == 0);
    }

    public static int ReservedVisitorCount(IEnumerable<CareHomeVisit> reservations,
        DateTime startsAtUtc, DateTime endsAtUtc, DateTime utcNow)
    {
        return reservations
            .Where(x => HoldsCapacity(x, utcNow) && x.StartsAtUtc < endsAtUtc && x.EndsAtUtc > startsAtUtc)
            .GroupBy(x => x.RescheduleOfVisitId ?? x.Id)
            .Sum(group => group.Max(x => x.VisitorCount));
    }

    public static bool HoldsCapacity(CareHomeVisit visit, DateTime utcNow) =>
        visit.Status == CareHomeVisitStatus.Approved
        || visit.Status is CareHomeVisitStatus.Pending or CareHomeVisitStatus.ReschedulePending
            && visit.DecisionExpiresOnUtc > utcNow;

    private static TimeZoneInfo ResolveCairoTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(CairoTimeZoneId); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
    }
}

internal static class CareHomeVisitMapping
{
    public static CareHomeVisitSettingsResponse ToResponse(this CareHomeVisitSettings value) =>
        new(value.FacilityId.Value, value.Version, value.VisitorCapacity,
            value.OperatingHours, value.VisitingWindows, value.Closures);

    public static CareHomeVisitResponse ToResponse(this CareHomeVisit value) => new(
        value.Id, value.FacilityId.Value, value.FamilyId.Value, value.Kind,
        value.ElderlyId?.Value, value.VisitorName, value.VisitorPhone, value.VisitorCount,
        new DateTimeOffset(value.StartsAtUtc, TimeSpan.Zero), new DateTimeOffset(value.EndsAtUtc, TimeSpan.Zero),
        value.Status, value.RescheduleOfVisitId,
        new DateTimeOffset(value.DecisionExpiresOnUtc, TimeSpan.Zero),
        value.DecidedByUserId?.Value, value.DecidedOnUtc is DateTime decided ? new DateTimeOffset(decided, TimeSpan.Zero) : null,
        value.DecisionReason, value.CancelledByUserId?.Value,
        value.CancelledOnUtc is DateTime cancelled ? new DateTimeOffset(cancelled, TimeSpan.Zero) : null,
        value.CancellationReason, new DateTimeOffset(value.CreatedOnUtc, TimeSpan.Zero));
}
