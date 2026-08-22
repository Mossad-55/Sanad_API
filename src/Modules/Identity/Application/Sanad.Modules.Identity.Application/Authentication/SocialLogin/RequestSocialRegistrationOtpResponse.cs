namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed record RequestSocialRegistrationOtpResponse(
    string OpaqueRegistrationChallenge,
    DateTime ExpiresOnUtc);