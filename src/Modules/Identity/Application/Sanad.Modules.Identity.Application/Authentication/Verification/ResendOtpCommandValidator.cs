using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Authentication.Verification;

public sealed class ResendOtpCommandValidator :
    AbstractValidator<ResendOtpCommand>
{
    public ResendOtpCommandValidator()
    {
        RuleFor(command =>
                command.VerificationRequestId)
            .NotEqual(VerificationRequestId.Empty);
    }
}