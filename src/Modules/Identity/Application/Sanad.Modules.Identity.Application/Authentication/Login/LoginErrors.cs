using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Identity.Application.Authentication.Login;

public static class LoginErrors
{
    public static readonly Error InvalidCredentials =
        new(
            "Identity.Login.InvalidCredentials",
            "Email or password is invalid.");

    public static readonly Error UserSuspended =
        new(
            "Identity.Login.UserSuspended",
            "User account is suspended.");

    public static readonly Error UserBlocked =
        new(
            "Identity.Login.UserBlocked",
            "User account is blocked.");

    public static readonly Error SessionLimitReached =
        new(
            "Identity.Login.SessionLimitReached",
            "Maximum active device sessions reached.");
}