using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Enums;

namespace Ahlena.Modules.Caregivers.Application.Availability;

/// <summary>
/// One available interval for a caregiver on a date, as UTC instants. Stored
/// UTC timestamps are never recomputed or relabeled: the local declared window
/// is resolved through the documented display zone at read time only.
/// </summary>
public sealed record CaregiverDayAvailabilityInterval(
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

/// <summary>
/// Advisory date-specific availability. <see cref="TimeZoneId"/> is the
/// documented display zone (<c>Africa/Cairo</c>) the windows were declared in;
/// the intervals are UTC instants. The response is advisory, not a
/// reservation, and it never invents a hold/expiry rule.
/// </summary>
public sealed record CaregiverDayAvailabilityResponse(
    Guid CaregiverId,
    DateOnly Date,
    string TimeZoneId,
    BookingShiftType ShiftType,
    IReadOnlyList<CaregiverDayAvailabilityInterval> Intervals);

public sealed record GetCaregiverDayAvailabilityQuery(
    Guid CaregiverId,
    DateOnly Date,
    BookingShiftType ShiftType) : IQuery<CaregiverDayAvailabilityResponse>;
