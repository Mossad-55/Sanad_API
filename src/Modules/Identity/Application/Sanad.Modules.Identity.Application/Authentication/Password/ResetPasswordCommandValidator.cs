using FluentValidation;
using Sanad.Modules.Identity.Application.Authentication.Registration;

namespace Sanad.Modules.Identity.Application.Authentication.Password;

public sealed class ResetPasswordCommandValidator :
    AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(command =>
                command.Email)
            .NotEmpty()
            .MaximumLength(256)
            .EmailAddress();

        RuleFor(command =>
                command.OtpCode)
            .NotEmpty()
            .Matches(@"\A[0-9]{6}\z")
            .WithMessage(
                "Verification code must contain exactly six ASCII digits.");

        RuleFor(command =>
                command.NewPassword)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(
                RegisterUserCommandValidator
                    .MaximumPasswordLength)
            .Matches("[A-Z]")
            .WithMessage(
                "Password must contain an uppercase letter.")
            .Matches("[a-z]")
            .WithMessage(
                "Password must contain a lowercase letter.")
            .Matches("[0-9]")
            .WithMessage(
                "Password must contain a number.");
    }
}