using FluentValidation.TestHelper;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Authentication.Password;

namespace Sanad.UnitTests.Identity.Password;

public sealed class PasswordValidatorTests
{
    [Fact]
    public void RequestReset_Validate_ShouldAcceptValidCommand()
    {
        RequestPasswordResetCommandValidator validator =
            new();

        RequestPasswordResetCommand command =
            new("user@example.com");

        TestValidationResult<RequestPasswordResetCommand>
            result =
                validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequestReset_Validate_ShouldRejectEmptyEmail(
        string? email)
    {
        RequestPasswordResetCommandValidator validator =
            new();

        RequestPasswordResetCommand command =
            new(email!);

        validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.Email);
    }

    [Fact]
    public void RequestReset_Validate_ShouldRejectInvalidEmailFormat()
    {
        RequestPasswordResetCommandValidator validator =
            new();

        RequestPasswordResetCommand command =
            new("not-an-email");

        validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.Email);
    }

    [Fact]
    public void Reset_Validate_ShouldAcceptValidCommand()
    {
        ResetPasswordCommandValidator validator =
            new();

        ResetPasswordCommand command =
            new(
                "user@example.com",
                "123456",
                "NewPassword1");

        TestValidationResult<ResetPasswordCommand>
            result =
                validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Reset_Validate_ShouldRejectInvalidEmail(
        string? email)
    {
        ResetPasswordCommandValidator validator =
            new();

        ResetPasswordCommand command =
            new(email!, "123456", "NewPassword1");

        validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.Email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData("123 56")]
    public void Reset_Validate_ShouldRejectInvalidOtp(
        string? otp)
    {
        ResetPasswordCommandValidator validator =
            new();

        ResetPasswordCommand command =
            new("user@example.com", otp!, "NewPassword1");

        validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.OtpCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("nouppercase1")]
    [InlineData("NOLOWERCASE1")]
    [InlineData("NoDigitsHere")]
    public void Reset_Validate_ShouldRejectWeakPassword(
        string? password)
    {
        ResetPasswordCommandValidator validator =
            new();

        ResetPasswordCommand command =
            new("user@example.com", "123456", password!);

        validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.NewPassword);
    }

    [Fact]
    public void Reset_Validate_ShouldRejectLongPassword()
    {
        ResetPasswordCommandValidator validator =
            new();

        string longPassword =
            new string('A', 128) + "a1";

        ResetPasswordCommand command =
            new("user@example.com", "123456", longPassword);

        validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.NewPassword);
    }

    [Fact]
    public void Change_Validate_ShouldAcceptValidCommand()
    {
        ChangePasswordCommandValidator validator =
            new();

        ChangePasswordCommand command =
            new(
                UserId.New(),
                "CurrentPass1",
                "NewPassword1");

        TestValidationResult<ChangePasswordCommand>
            result =
                validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Change_Validate_ShouldRejectEmptyUserId()
    {
        ChangePasswordCommandValidator validator =
            new();

        ChangePasswordCommand command =
            new(
                UserId.Empty,
                "CurrentPass1",
                "NewPassword1");

        validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.CurrentUserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Change_Validate_ShouldRejectEmptyCurrentPassword(
        string? currentPassword)
    {
        ChangePasswordCommandValidator validator =
            new();

        ChangePasswordCommand command =
            new(
                UserId.New(),
                currentPassword!,
                "NewPassword1");

        validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.CurrentPassword);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("nouppercase1")]
    [InlineData("NOLOWERCASE1")]
    [InlineData("NoDigitsHere")]
    public void Change_Validate_ShouldRejectWeakNewPassword(
        string? newPassword)
    {
        ChangePasswordCommandValidator validator =
            new();

        ChangePasswordCommand command =
            new(
                UserId.New(),
                "CurrentPass1",
                newPassword!);

        validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.NewPassword);
    }

    [Fact]
    public void Change_Validate_ShouldRejectLongNewPassword()
    {
        ChangePasswordCommandValidator validator =
            new();

        string longPassword =
            new string('A', 128) + "a1";

        ChangePasswordCommand command =
            new(
                UserId.New(),
                "CurrentPass1",
                longPassword);

        validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.NewPassword);
    }
}