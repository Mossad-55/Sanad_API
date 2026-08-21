using FluentValidation;

namespace Sanad.Modules.Identity.Application.Authentication.Password;

public sealed class RequestPasswordResetCommandValidator :
    AbstractValidator<RequestPasswordResetCommand>
{
    public RequestPasswordResetCommandValidator()
    {
        RuleFor(command =>
                command.Email)
            .NotEmpty()
            .EmailAddress();
    }
}