using FluentValidation;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed class StartSocialLoginCommandValidator :
    AbstractValidator<StartSocialLoginCommand>
{
    public const int MaximumProviderCredentialLength =
        16_384;

    public StartSocialLoginCommandValidator()
    {
        RuleFor(command =>
                command.Provider)
            .Must(IsSupportedProvider)
            .WithMessage(
                "Only Google and Apple are supported.");

        RuleFor(command =>
                command.ProviderCredential)
            .NotEmpty()
            .MaximumLength(
                MaximumProviderCredentialLength);

        RuleFor(command =>
                command.Nonce)
            .NotEmpty()
            .Length(
                ExternalAuthenticationNoncePolicy
                    .EncodedLength);

        RuleFor(command =>
                command.DeviceName)
            .NotEmpty()
            .MaximumLength(
                DeviceSession.MaximumDeviceNameLength);

        RuleFor(command =>
                command.DevicePlatform)
            .Must(platform =>
                Enum.IsDefined(platform) &&
                platform !=
                    DevicePlatform.Unknown)
            .WithMessage(
                "Device platform is invalid.");

        RuleFor(command =>
                command.AppVersion)
            .NotEmpty()
            .MaximumLength(
                DeviceSession.MaximumAppVersionLength);
    }

    private static bool IsSupportedProvider(
        ExternalLoginProvider provider)
    {
        return provider is
            ExternalLoginProvider.Google or
            ExternalLoginProvider.Apple;
    }
}