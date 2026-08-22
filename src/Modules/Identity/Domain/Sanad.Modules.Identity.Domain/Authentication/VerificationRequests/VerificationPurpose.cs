namespace Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;

public enum VerificationPurpose
{
    ElderlyLogin = 1,
    VerifyPhone = 2,
    VerifyEmail = 3,
    ResetPassword = 4,
    ConfirmExternalLoginLink = 5
}