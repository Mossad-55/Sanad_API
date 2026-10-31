using FluentValidation;

namespace Ahlena.Modules.Identity.Application.Authentication.Password;

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