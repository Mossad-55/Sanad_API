using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Caregivers.Application.HelpRequests;

public static class CaregiverErrors
{
    public static readonly Error NotFound =
        new(
            "Caregivers.HelpRequest.NotFound",
            "The caregiver profile was not found.");
}