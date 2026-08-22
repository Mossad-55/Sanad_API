using FluentValidation;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed class CompleteSocialRegistrationCommandValidator :
    AbstractValidator<CompleteSocialRegistrationCommand>
{
    public const int MaximumOpaqueChallengeLength = 1_024;

    public CompleteSocialRegistrationCommandValidator()
    {
        RuleFor(command =>
                command.OpaqueRegistrationChallenge)
            .NotEmpty()
            .MaximumLength(
                MaximumOpaqueChallengeLength);

        RuleFor(command =>
                command.Code)
            .NotEmpty()
            .Matches(
                @"^[0-9]{6}$")
            .WithMessage(
                "Verification code must contain exactly six ASCII digits.");

        RuleFor(command =>
                command.DeviceName)
            .NotEmpty()
            .MaximumLength(
                DeviceSession.MaximumDeviceNameLength);

        RuleFor(command =>
                command.DevicePlatform)
            .Must(platform =>
                Enum.IsDefined(platform) &&
                platform != DevicePlatform.Unknown)
            .WithMessage(
                "Device platform is invalid.");

        RuleFor(command =>
                command.AppVersion)
            .NotEmpty()
            .MaximumLength(
                DeviceSession.MaximumAppVersionLength);
    }
}