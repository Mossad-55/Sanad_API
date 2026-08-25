using FluentValidation;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.Modules.Identity.Application.Authentication.ElderlyLogin;

public sealed class VerifyElderlyLoginOtpCommandValidator :
    AbstractValidator<VerifyElderlyLoginOtpCommand>
{
    public VerifyElderlyLoginOtpCommandValidator()
    {
        RuleFor(command =>
                command.PhoneNumber)
            .NotEmpty()
            .Matches(
                @"\A\+[1-9][0-9]{1,14}\z")
            .WithMessage(
                "Phone number must use E.164 format.");

        RuleFor(command =>
                command.Code)
            .NotEmpty()
            .Matches(
                @"\A[0-9]{6}\z")
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