using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.API.Controllers.Requests;

public sealed record CreateServiceRequest(
    string ArabicName,
    string EnglishName,
    CaregiverType CaregiverType,
    bool IsActive);