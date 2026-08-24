using FluentValidation.TestHelper;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.UnitTests.Identity.SocialLogin;

public sealed class StartSocialLoginCommandValidatorTests
{
    [Theory]
    [InlineData(ExternalLoginProvider.Google)]
    [InlineData(ExternalLoginProvider.Apple)]
    public void Validate_ShouldAcceptGoogleAndApple(
        ExternalLoginProvider provider)
    {
        var validator =
            new StartSocialLoginCommandValidator();

        TestValidationResult<StartSocialLoginCommand> result =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    Provider = provider
                });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ShouldRejectUnsupportedProvider()
    {
        var validator =
            new StartSocialLoginCommandValidator();

        TestValidationResult<StartSocialLoginCommand> result =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    Provider =
                        (ExternalLoginProvider)999
                });

        result.ShouldHaveValidationErrorFor(
            command => command.Provider);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ShouldRejectMissingProviderCredential(
        string providerCredential)
    {
        var validator =
            new StartSocialLoginCommandValidator();

        TestValidationResult<StartSocialLoginCommand> result =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    ProviderCredential =
                        providerCredential
                });

        result.ShouldHaveValidationErrorFor(
            command =>
                command.ProviderCredential);
    }

    [Fact]
    public void Validate_ShouldRejectOversizedProviderCredential()
    {
        var validator =
            new StartSocialLoginCommandValidator();

        string oversizedCredential =
            new(
                'x',
                StartSocialLoginCommandValidator
                    .MaximumProviderCredentialLength + 1);

        TestValidationResult<StartSocialLoginCommand> result =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    ProviderCredential =
                        oversizedCredential
                });

        result.ShouldHaveValidationErrorFor(
            command =>
                command.ProviderCredential);
    }

    [Fact]
    public void Validate_ShouldRejectUnknownDevicePlatform()
    {
        var validator =
            new StartSocialLoginCommandValidator();

        TestValidationResult<StartSocialLoginCommand> result =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    DevicePlatform =
                        DevicePlatform.Unknown
                });

        result.ShouldHaveValidationErrorFor(
            command =>
                command.DevicePlatform);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ShouldRejectMissingDeviceName(
        string deviceName)
    {
        var validator =
            new StartSocialLoginCommandValidator();

        TestValidationResult<StartSocialLoginCommand> result =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    DeviceName = deviceName
                });

        result.ShouldHaveValidationErrorFor(
            command =>
                command.DeviceName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ShouldRejectMissingAppVersion(
        string appVersion)
    {
        var validator =
            new StartSocialLoginCommandValidator();

        TestValidationResult<StartSocialLoginCommand> result =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    AppVersion = appVersion
                });

        result.ShouldHaveValidationErrorFor(
            command =>
                command.AppVersion);
    }

    private static StartSocialLoginCommand CreateValidCommand()
    {
        return new StartSocialLoginCommand(
            ExternalLoginProvider.Google,
            "provider-credential",
            "Ahmed's iPhone",
            DevicePlatform.iOS,
            "1.0.0",
            new string(
                'n',
                ExternalAuthenticationNoncePolicy
                    .EncodedLength));
    }
}