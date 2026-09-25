using FluentValidation.TestHelper;
using Sanad.Modules.Identity.Application.Authentication.Login;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.UnitTests.Identity.Login;

public sealed class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator
        _validator = new();

    [Fact]
    public void Validate_ShouldAcceptValidCommand()
    {
        LoginCommand command =
            CreateValidCommand();

        TestValidationResult<LoginCommand>
            result =
                _validator.TestValidate(
                    command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid-email")]
    [InlineData("201001234567")]
    [InlineData("+0123456789")]
    [InlineData("+201 001234567")]
    public void Validate_ShouldRejectInvalidIdentifier(
        string identifier)
    {
        LoginCommand command =
            CreateValidCommand() with
            {
                Identifier = identifier
            };

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.Identifier);
    }

    [Theory]
    [InlineData("mohamed@example.com")]
    [InlineData("+201001234567")]
    public void Validate_ShouldAcceptEmailOrE164PhoneIdentifier(
        string identifier)
    {
        LoginCommand command =
            CreateValidCommand() with
            {
                Identifier = identifier
            };

        _validator
            .TestValidate(command)
            .ShouldNotHaveValidationErrorFor(
                value =>
                    value.Identifier);
    }

    [Fact]
    public void Validate_ShouldRejectMissingPassword()
    {
        LoginCommand command =
            CreateValidCommand() with
            {
                Password = ""
            };

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.Password);
    }

    [Theory]
    [InlineData(DevicePlatform.Unknown)]
    [InlineData((DevicePlatform)999)]
    public void Validate_ShouldRejectInvalidPlatform(
        DevicePlatform platform)
    {
        LoginCommand command =
            CreateValidCommand() with
            {
                DevicePlatform = platform
            };

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.DevicePlatform);
    }

    [Fact]
    public void Validate_ShouldRejectMissingDeviceName()
    {
        LoginCommand command =
            CreateValidCommand() with
            {
                DeviceName = ""
            };

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.DeviceName);
    }

    [Fact]
    public void Validate_ShouldRejectMissingAppVersion()
    {
        LoginCommand command =
            CreateValidCommand() with
            {
                AppVersion = ""
            };

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.AppVersion);
    }

    private static LoginCommand CreateValidCommand()
    {
        return new LoginCommand(
            Identifier: "mohamed@example.com",
            Password: "StrongPass123",
            DeviceName: "iPhone 16",
            DevicePlatform: DevicePlatform.iOS,
            AppVersion: "1.0.0");
    }
}
