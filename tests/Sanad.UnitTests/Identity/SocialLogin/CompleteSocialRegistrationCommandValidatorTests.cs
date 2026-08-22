using FluentValidation.TestHelper;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.UnitTests.Identity.SocialLogin;

public sealed class CompleteSocialRegistrationCommandValidatorTests
{
    [Fact]
    public void Validate_ShouldAcceptValidCommand()
    {
        new CompleteSocialRegistrationCommandValidator()
            .TestValidate(CreateCommand())
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("١٢٣٤٥٦")]
    public void Validate_ShouldRejectInvalidOtpCode(string code)
    {
        new CompleteSocialRegistrationCommandValidator()
            .TestValidate(CreateCommand() with { Code = code })
            .ShouldHaveValidationErrorFor(command => command.Code);
    }

    [Fact]
    public void Validate_ShouldRejectMissingChallenge()
    {
        new CompleteSocialRegistrationCommandValidator()
            .TestValidate(CreateCommand() with { OpaqueRegistrationChallenge = string.Empty })
            .ShouldHaveValidationErrorFor(command => command.OpaqueRegistrationChallenge);
    }

    [Fact]
    public void Validate_ShouldRejectUnknownPlatform()
    {
        new CompleteSocialRegistrationCommandValidator()
            .TestValidate(CreateCommand() with { DevicePlatform = DevicePlatform.Unknown })
            .ShouldHaveValidationErrorFor(command => command.DevicePlatform);
    }

    private static CompleteSocialRegistrationCommand CreateCommand()
    {
        return new CompleteSocialRegistrationCommand(
            "registration-challenge", "123456", "Ahmed's iPhone",
            DevicePlatform.iOS, "1.0.0");
    }
}
