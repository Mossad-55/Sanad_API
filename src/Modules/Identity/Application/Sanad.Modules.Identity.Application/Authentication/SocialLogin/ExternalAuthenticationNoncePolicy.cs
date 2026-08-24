namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public static class ExternalAuthenticationNoncePolicy
{
    public static readonly TimeSpan Lifetime =
        TimeSpan.FromMinutes(5);
}