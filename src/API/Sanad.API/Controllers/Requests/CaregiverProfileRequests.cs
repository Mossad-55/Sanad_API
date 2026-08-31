using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.API.Controllers.Requests;

public sealed record UpdateMedicalProfileRequest(
    Guid ProfessionalTitleId,
    int YearsOfExperience,
    Guid SpecializationId,
    Guid AcademicDegreeId,
    string? CurrentWorkplace,
    string? Biography);

public sealed record UpdateCompanionProfileRequest(
    int YearsOfExperience,
    Guid SpecializationId,
    string? Biography);

public sealed record UpdateCaregiverAddressRequest(
    string? DetailedAddress);

public sealed record UpdateCaregiverSelectionsRequest(
    IReadOnlyList<Guid> ServiceIds,
    IReadOnlyList<Guid> LanguageIds,
    IReadOnlyList<Guid> AreaIds);

public sealed record UpdateMedicalPricingRequest(
    decimal HomeVisitPrice,
    decimal EightHourShiftPrice,
    decimal TwelveHourShiftPrice,
    decimal TwentyFourHourShiftPrice);

public sealed record UpdateCompanionPricingRequest(
    decimal HourlyPrice,
    decimal EightHourDayPrice,
    decimal OvernightPrice);

public sealed record MedicalShiftRequest(
    int DayOfWeek,
    int ShiftType);

public sealed record MedicalHomeVisitWindowRequest(
    int DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);

public sealed record UpdateMedicalScheduleRequest(
    IReadOnlyList<MedicalShiftRequest>? Shifts,
    IReadOnlyList<MedicalHomeVisitWindowRequest>? HomeVisitWindows);

public sealed record CompanionAvailabilityWindowRequest(
    int BookingType,
    int DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);

public sealed record UpdateCompanionScheduleRequest(
    IReadOnlyList<CompanionAvailabilityWindowRequest>? Windows);

public sealed record AddCertificateRequest(
    CaregiverCertificateType Type,
    DateOnly? ExpiryDate);

public sealed record ReplaceCertificateFileRequest(
    DateOnly? ExpiryDate);

public sealed record ReviewCertificateRequest(
    string Reason);

public sealed record ReviewCaregiverRequest(
    string Reason);