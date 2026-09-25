using System.Text.RegularExpressions;
using FluentValidation;
using Sanad.Modules.Identity.Application.Authentication.Registration;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.Modules.Identity.Application.Authentication.Login;

public sealed class LoginCommandValidator :
    AbstractValidator<LoginCommand>
{
    private static readonly Regex EmailRegex =
        new(
            @"\A[^@\s]+@[^@\s]+\.[^@\s]+\z",
            RegexOptions.Compiled);

    private static readonly Regex E164PhoneNumberRegex =
        new(
            @"\A\+[1-9][0-9]{1,14}\z",
            RegexOptions.Compiled);

    public LoginCommandValidator()
    {
        RuleFor(command =>
                command.Identifier)
            .NotEmpty()
            .MaximumLength(256)
            .Must(IsEmailOrE164PhoneNumber)
            .WithMessage(
                "Identifier must be a valid email address or E.164 phone number.");

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

    private static bool IsEmailOrE164PhoneNumber(
        string identifier)
    {
        return EmailRegex.IsMatch(identifier) ||
            E164PhoneNumberRegex.IsMatch(identifier);
    }
}
