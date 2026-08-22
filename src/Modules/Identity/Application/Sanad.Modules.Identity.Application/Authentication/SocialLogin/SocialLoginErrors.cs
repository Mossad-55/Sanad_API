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

    public static readonly Error ExternalLinkConfirmationFailed =
        new(
            "Identity.SocialLogin.ExternalLinkConfirmationFailed",
            "External login confirmation failed.");

    public static readonly Error SocialRegistrationFailed =
        new(
            "Identity.SocialLogin.RegistrationFailed",
            "Social registration failed.");

    public static readonly Error ExternalLinkFailed =
        new(
            "Identity.SocialLogin.ExternalLinkFailed",
            "External login linking failed.");
}