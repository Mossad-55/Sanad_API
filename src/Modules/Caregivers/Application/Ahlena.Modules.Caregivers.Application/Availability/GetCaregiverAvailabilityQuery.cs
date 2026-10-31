using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Caregivers.Application.Availability;

public sealed record CaregiverAvailabilityResponse(
    Guid CaregiverId,
    string NameArabic,
    string NameEnglish,
    string? LicenseNumber,
    CaregiverAvailabilityStatus Status,
    DateTime? LastSeen,
    IReadOnlyList<AvailabilitySlot> AvailableSlots);

public sealed record AvailabilitySlot(
    TimeOnly StartTime,
    TimeOnly EndTime,
    DayOfWeek Day);

public enum CaregiverAvailabilityStatus { Active = 1, Inactive = 2, OnLeave = 3, Busy = 4 }

public sealed record GetCaregiverAvailabilityQuery(Guid CaregiverId) : IQuery<CaregiverAvailabilityResponse>;
