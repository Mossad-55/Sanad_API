namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public static class ExternalAuthenticationNoncePolicy
{
    public const int ByteLength = 32;

    public const int EncodedLength = 43;

    public static readonly TimeSpan Lifetime =
        TimeSpan.FromMinutes(5);
}