using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Identity.Application.Authentication.ElderlyLogin;

public static class ElderlyLoginErrors
{
    public static readonly Error OtpVerificationFailed =
        new(
            "Identity.ElderlyLogin.OtpVerificationFailed",
            "OTP verification failed.");

    public static readonly Error SessionLimitReached =
        new(
            "Identity.ElderlyLogin.SessionLimitReached",
            "Maximum active device sessions reached.");
}