using FluentValidation.TestHelper;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.UnitTests.Identity.SocialLogin;

public sealed class LinkExternalLoginCommandValidatorTests
{
    [Theory]
    [InlineData(ExternalLoginProvider.Google)]
    [InlineData(ExternalLoginProvider.Apple)]
    public void Validate_ShouldAcceptSupportedProvider(ExternalLoginProvider provider)
    {
        new LinkExternalLoginCommandValidator()
            .TestValidate(CreateCommand() with { Provider = provider })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ShouldRejectEmptyUserId()
    {
        new LinkExternalLoginCommandValidator()
            .TestValidate(CreateCommand() with { UserId = UserId.Empty })
            .ShouldHaveValidationErrorFor(command => command.UserId);
    }

    [Fact]
    public void Validate_ShouldRejectUnsupportedProvider()
    {
        new LinkExternalLoginCommandValidator()
            .TestValidate(CreateCommand() with { Provider = (ExternalLoginProvider)999 })
            .ShouldHaveValidationErrorFor(command => command.Provider);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ShouldRejectMissingCredential(string credential)
    {
        new LinkExternalLoginCommandValidator()
            .TestValidate(CreateCommand() with { ProviderCredential = credential })
            .ShouldHaveValidationErrorFor(command => command.ProviderCredential);
    }

    private static LinkExternalLoginCommand CreateCommand()
    {
        return new LinkExternalLoginCommand(
            UserId.New(),
            ExternalLoginProvider.Google,
            "provider-credential",
            new string(
                'n',
                ExternalAuthenticationNoncePolicy
                    .EncodedLength));
    }

}
