using FluentValidation;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed class RequestSocialRegistrationOtpCommandValidator :
    AbstractValidator<RequestSocialRegistrationOtpCommand>
{
    public const int MaximumOpaqueChallengeLength = 1_024;

    public RequestSocialRegistrationOtpCommandValidator()
    {
        RuleFor(command =>
                command.OpaqueChallenge)
            .NotEmpty()
            .MaximumLength(
                MaximumOpaqueChallengeLength);

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
                command.AccountType)
            .Must(IsSupportedAccountType)
            .WithMessage(
                "Social registration supports Family, " +
                "Medical Caregiver, or Companion Caregiver only.");

        RuleFor(command =>
                command.PhoneNumber)
            .NotEmpty()
            .Matches(
                @"^\+[1-9]\d{1,14}$")
            .WithMessage(
                "Phone number must use E.164 format.");
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