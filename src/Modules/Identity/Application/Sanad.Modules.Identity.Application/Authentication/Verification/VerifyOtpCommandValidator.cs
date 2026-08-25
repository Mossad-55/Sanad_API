using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Authentication.Verification;

public sealed class VerifyOtpCommandValidator :
    AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpCommandValidator()
    {
        RuleFor(command =>
                command.VerificationRequestId)
            .NotEqual(
                VerificationRequestId.Empty);

        RuleFor(command =>
                command.Code)
            .NotEmpty()
            .Matches(
                @"\A[0-9]{6}\z")
            .WithMessage(
                "Verification code must contain exactly six ASCII digits.");
    }
}