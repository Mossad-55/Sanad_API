using FluentValidation;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Authentication.Registration;

public sealed class RegisterUserCommandValidator :
    AbstractValidator<RegisterUserCommand>
{
    public const int MaximumPasswordLength = 128;
    public const int MaximumAvatarUrlLength = 500;

    public RegisterUserCommandValidator()
    {
        RuleFor(command =>
                command.ArabicFullName)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(200);

        RuleFor(command =>
                command.EnglishFullName)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(200);

        RuleFor(command =>
                command.Email)
            .NotEmpty()
            .MaximumLength(256)
            .EmailAddress();

        RuleFor(command =>
                command.PhoneNumber)
            .NotEmpty()
            .Matches(
                @"^\+[1-9]\d{1,14}$")
            .WithMessage(
                "Phone number must use E.164 format.");

        RuleFor(command =>
                command.Password)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(
                MaximumPasswordLength)
            .Matches("[A-Z]")
            .WithMessage(
                "Password must contain an uppercase letter.")
            .Matches("[a-z]")
            .WithMessage(
                "Password must contain a lowercase letter.")
            .Matches("[0-9]")
            .WithMessage(
                "Password must contain a number.");

        RuleFor(command =>
                command.AccountType)
            .Must(IsSupportedAccountType)
            .WithMessage(
                "Registration supports Family, " +
                "Medical Caregiver, or Companion Caregiver only.");

        RuleFor(command =>
                command.AvatarUrl)
            .MaximumLength(
                MaximumAvatarUrlLength)
            .When(command =>
                !string.IsNullOrWhiteSpace(
                    command.AvatarUrl));
    }

    private static bool IsSupportedAccountType(
        AccountType accountType)
    {
        return accountType is
            AccountType.Family or
            AccountType.MedicalCaregiver or
            AccountType.CompanionCaregiver;
    }
}