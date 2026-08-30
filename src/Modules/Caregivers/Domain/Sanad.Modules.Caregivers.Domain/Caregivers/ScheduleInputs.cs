namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed record MedicalShiftInput(
    DayOfWeek DayOfWeek,
    MedicalShiftType ShiftType);

public sealed record MedicalHomeVisitWindowInput(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);

public sealed record CompanionAvailabilityWindowInput(
    CompanionBookingType BookingType,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);