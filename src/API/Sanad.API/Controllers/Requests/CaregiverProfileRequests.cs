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