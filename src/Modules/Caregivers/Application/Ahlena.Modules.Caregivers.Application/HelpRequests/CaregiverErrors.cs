using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.Modules.Caregivers.Application.HelpRequests;

public static class CaregiverErrors
{
    public static readonly Error NotFound =
        new(
            "Caregivers.HelpRequest.NotFound",
            "The caregiver profile was not found.");
}