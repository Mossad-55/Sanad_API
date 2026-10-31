using FluentValidation;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Authentication.Verification;

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