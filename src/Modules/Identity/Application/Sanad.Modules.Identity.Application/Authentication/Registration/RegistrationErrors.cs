using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Identity.Application.Authentication.Registration;

public static class RegistrationErrors
{
    public static readonly Error EmailAlreadyInUse =
        new(
            "Identity.Registration.EmailAlreadyInUse",
            "Email is already registered.");

    public static readonly Error PhoneAlreadyInUse =
        new(
            "Identity.Registration.PhoneAlreadyInUse",
            "Phone number is already registered.");

    public static readonly Error UnsupportedAccountType =
        new(
            "Identity.Registration.UnsupportedAccountType",
            "This account type is not supported by " +
            "non-Elderly registration.");
}