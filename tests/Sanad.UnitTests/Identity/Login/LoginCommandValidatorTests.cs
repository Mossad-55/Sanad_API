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
    public void Validate_ShouldRejectInvalidEmail(
        string email)
    {
        LoginCommand command =
            CreateValidCommand() with
            {
                Email = email
            };

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.Email);
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
            Email: "mohamed@example.com",
            Password: "StrongPass123",
            DeviceName: "iPhone 16",
            DevicePlatform: DevicePlatform.iOS,
            AppVersion: "1.0.0");
    }
}