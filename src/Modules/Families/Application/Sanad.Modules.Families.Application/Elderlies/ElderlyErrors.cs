using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Families.Application.Elderlies;

public static class ElderlyErrors
{
    public static readonly Error FamilyNotFound =
        new("Families.Elderly.FamilyNotFound",
            "The family was not found.");

    public static readonly Error PhoneLinkedToAnotherFamily =
        new("Families.Elderly.PhoneLinkedToAnotherFamily",
            "This dependent is already linked to another family.");

    public static readonly Error PhoneBelongsToNonElderly =
        new("Families.Elderly.PhoneBelongsToNonElderly",
            "This phone number belongs to a non-elderly account.");

    public static readonly Error NotFound =
        new("Families.Elderly.NotFound",
            "The dependent was not found.");

    public static readonly Error IdentityCreationFailed =
        new("Families.Elderly.IdentityCreationFailed",
            "The elderly login could not be created.");

    public static readonly Error InvalidProfile =
        new("Families.Elderly.InvalidProfile",
            "The dependent profile is invalid.");
}