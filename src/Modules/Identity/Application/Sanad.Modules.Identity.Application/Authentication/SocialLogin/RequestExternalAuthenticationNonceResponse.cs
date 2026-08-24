namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed record RequestExternalAuthenticationNonceResponse(
    string Nonce,
    DateTime ExpiresOnUtc);