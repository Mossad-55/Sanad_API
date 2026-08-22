namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public static class SocialLoginPolicy
{
    public static readonly TimeSpan ChallengeLifetime =
        TimeSpan.FromMinutes(10);
}