using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed class LinkExternalLoginCommandValidator :
    AbstractValidator<LinkExternalLoginCommand>
{
    public const int MaximumProviderCredentialLength = 16_384;

    public LinkExternalLoginCommandValidator()
    {
        RuleFor(command =>
                command.UserId)
            .NotEqual(UserId.Empty);

        RuleFor(command =>
                command.Provider)
            .Must(provider =>
                provider is
                    ExternalLoginProvider.Google or
                    ExternalLoginProvider.Apple)
            .WithMessage(
                "Only Google and Apple are supported.");

        RuleFor(command =>
                command.ProviderCredential)
            .NotEmpty()
            .MaximumLength(
                MaximumProviderCredentialLength);
    }
}