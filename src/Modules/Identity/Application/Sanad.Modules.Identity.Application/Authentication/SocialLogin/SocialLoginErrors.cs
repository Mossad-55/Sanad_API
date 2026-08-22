using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public static class SocialLoginErrors
{
    public static readonly Error AuthenticationFailed =
        new(
            "Identity.SocialLogin.AuthenticationFailed",
            "Social authentication failed.");

    public static readonly Error SessionLimitReached =
        new(
            "Identity.SocialLogin.SessionLimitReached",
            "Maximum active device sessions reached.");
}