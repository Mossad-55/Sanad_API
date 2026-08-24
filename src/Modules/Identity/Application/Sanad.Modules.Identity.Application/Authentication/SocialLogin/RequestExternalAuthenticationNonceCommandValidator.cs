using FluentValidation;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed class RequestExternalAuthenticationNonceCommandValidator :
    AbstractValidator<RequestExternalAuthenticationNonceCommand>
{
    public RequestExternalAuthenticationNonceCommandValidator()
    {
        RuleFor(command =>
                command.Provider)
            .Must(provider =>
                provider is
                    ExternalLoginProvider.Google or
                    ExternalLoginProvider.Apple)
            .WithMessage(
                "Only Google and Apple are supported.");
    }
}