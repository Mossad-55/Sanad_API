using FluentValidation.TestHelper;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.UnitTests.Identity.SocialLogin;

public sealed class ConfirmExternalLoginLinkCommandValidatorTests
{
    [Fact]
    public void Validate_ShouldAcceptValidCommand()
    {
        var validator = new ConfirmExternalLoginLinkCommandValidator();

        validator.TestValidate(CreateCommand())
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ShouldRejectMissingChallenge(string opaqueChallenge)
    {
        var validator = new ConfirmExternalLoginLinkCommandValidator();

        validator.TestValidate(CreateCommand() with { OpaqueChallenge = opaqueChallenge })
            .ShouldHaveValidationErrorFor(command => command.OpaqueChallenge);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("١٢٣٤٥٦")]
    public void Validate_ShouldRejectNonAsciiSixDigitCode(string code)
    {
        var validator = new ConfirmExternalLoginLinkCommandValidator();

        validator.TestValidate(CreateCommand() with { Code = code })
            .ShouldHaveValidationErrorFor(command => command.Code);
    }

    [Fact]
    public void Validate_ShouldRejectUnknownPlatform()
    {
        var validator = new ConfirmExternalLoginLinkCommandValidator();

        validator.TestValidate(CreateCommand() with { DevicePlatform = DevicePlatform.Unknown })
            .ShouldHaveValidationErrorFor(command => command.DevicePlatform);
    }

    private static ConfirmExternalLoginLinkCommand CreateCommand()
    {
        return new ConfirmExternalLoginLinkCommand(
            "opaque-challenge",
            "123456",
            "Ahmed's iPhone",
            DevicePlatform.iOS,
            "1.0.0");
    }
}
