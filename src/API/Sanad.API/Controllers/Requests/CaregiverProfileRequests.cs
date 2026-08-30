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