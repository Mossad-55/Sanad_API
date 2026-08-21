using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Identity.Application.Authentication.Password;

public static class PasswordErrors
{
    public static readonly Error UserNotFound =
        new(
            "Identity.Password.UserNotFound",
            "User was not found.");

    public static readonly Error UserNotActive =
        new(
            "Identity.Password.UserNotActive",
            "User account is not active.");

    public static readonly Error UserHasNoPassword =
        new(
            "Identity.Password.UserHasNoPassword",
            "User does not have a password set.");

    public static readonly Error InvalidCurrentPassword =
        new(
            "Identity.Password.InvalidCurrentPassword",
            "Current password is incorrect.");

    public static readonly Error OtpVerificationFailed =
        new(
            "Identity.Password.OtpVerificationFailed",
            "OTP verification failed.");

    public static readonly Error PendingRequestNotFound =
        new(
            "Identity.Password.PendingRequestNotFound",
            "No pending password reset request found.");

    public static readonly Error NewPasswordMustDiffer =
        new(
            "Identity.Password.NewPasswordMustDiffer",
            "New password must differ from the current password.");
}