namespace Sanad.Modules.Identity.Domain.Authentication.OtpCodes;

public enum OtpPurpose
{
    Login = 1,
    VerifyPhone = 2,
    VerifyEmail = 3,
    ResetPassword = 4
}