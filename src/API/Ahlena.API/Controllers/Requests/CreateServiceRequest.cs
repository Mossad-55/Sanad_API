using Ahlena.Modules.Caregivers.Domain.Caregivers;

namespace Ahlena.API.Controllers.Requests;

public sealed record CreateServiceRequest(
    string ArabicName,
    string EnglishName,
    CaregiverType CaregiverType,
    bool IsActive);