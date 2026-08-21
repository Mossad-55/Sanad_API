using FluentValidation;
using Sanad.Modules.Identity.Application.Authentication.Registration;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.Modules.Identity.Application.Authentication.Login;

public sealed class LoginCommandValidator :
    AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(command =>
                command.Email)
            .NotEmpty()
            .MaximumLength(256)
            .EmailAddress();

        RuleFor(command =>
                command.Password)
            .NotEmpty()
            .MaximumLength(
                RegisterUserCommandValidator
                    .MaximumPasswordLength);

        RuleFor(command =>
                command.DeviceName)
            .NotEmpty()
            .MaximumLength(
                DeviceSession
                    .MaximumDeviceNameLength);

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
                DeviceSession
                    .MaximumAppVersionLength);
    }
}