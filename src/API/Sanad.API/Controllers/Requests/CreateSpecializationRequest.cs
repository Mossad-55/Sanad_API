using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.API.Controllers.Requests;

public sealed record CreateSpecializationRequest(
    string ArabicName,
    string EnglishName,
    bool IsActive,
    CaregiverType CaregiverType);