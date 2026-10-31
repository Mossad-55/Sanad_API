using Ahlena.Modules.Caregivers.Domain.Caregivers;

namespace Ahlena.API.Controllers.Requests;

public sealed record CreateSpecializationRequest(
    string ArabicName,
    string EnglishName,
    bool IsActive,
    CaregiverType CaregiverType);