using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Identity.Application.Users;

public static class ElderlyIdentityErrors
{
    public static readonly Error PhoneAlreadyInUse =
        new(
            "Identity.Elderly.PhoneAlreadyInUse",
            "The phone number is already linked to a non-elderly account.");

    public static readonly Error ElderlyUserNotFound =
        new(
            "Identity.Elderly.NotFound",
            "No elderly account exists for this phone number.");

    public static readonly Error InvalidProfile =
        new("Identity.Elderly.InvalidProfile",
            "The elderly profile is invalid.");
}