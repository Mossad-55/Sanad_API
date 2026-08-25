using FluentValidation.TestHelper;
using Sanad.Modules.Identity.Application.Authentication.ElderlyLogin;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.UnitTests.Identity.ElderlyLogin;

public sealed class ElderlyLoginCommandValidatorTests
{
    [Fact]
    public void RequestValidator_ShouldAcceptValidE164PhoneNumber()
    {
        var validator =
            new RequestElderlyLoginOtpCommandValidator();

        TestValidationResult<RequestElderlyLoginOtpCommand> result =
            validator.TestValidate(
                new RequestElderlyLoginOtpCommand(
                    "+201001234567"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("201001234567")]
    [InlineData("+001001234567")]
    [InlineData("+20 1001234567")]
    [InlineData("+201001234567890123")]
    [InlineData("+2٠١٠٠١٢٣٤٥٦٧")]
    public void RequestValidator_ShouldRejectInvalidPhoneNumber(
        string phoneNumber)
    {
        var validator =
            new RequestElderlyLoginOtpCommandValidator();

        TestValidationResult<RequestElderlyLoginOtpCommand> result =
            validator.TestValidate(
                new RequestElderlyLoginOtpCommand(
                    phoneNumber));

        result.ShouldHaveValidationErrorFor(
            command => command.PhoneNumber);
    }

    [Fact]
    public void VerifyValidator_ShouldAcceptValidCommand()
    {
        var validator =
            new VerifyElderlyLoginOtpCommandValidator();

        TestValidationResult<VerifyElderlyLoginOtpCommand> result =
            validator.TestValidate(
                CreateValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12 456")]
    [InlineData("١٢٣٤٥٦")]
    [InlineData("１２３４５６")]
    [InlineData("123456\n")]
    public void VerifyValidator_ShouldRejectNonAsciiSixDigitCode(
        string code)
    {
        var validator =
            new VerifyElderlyLoginOtpCommandValidator();

        TestValidationResult<VerifyElderlyLoginOtpCommand> result =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    Code = code
                });

        result.ShouldHaveValidationErrorFor(
            command => command.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("201001234567")]
    [InlineData("+20 1001234567")]
    [InlineData("+201001234567\n")]
    public void VerifyValidator_ShouldRejectInvalidPhoneNumber(
        string phoneNumber)
    {
        var validator =
            new VerifyElderlyLoginOtpCommandValidator();

        TestValidationResult<VerifyElderlyLoginOtpCommand> result =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    PhoneNumber = phoneNumber
                });

        result.ShouldHaveValidationErrorFor(
            command => command.PhoneNumber);
    }

    [Fact]
    public void VerifyValidator_ShouldRejectMissingDeviceName()
    {
        var validator =
            new VerifyElderlyLoginOtpCommandValidator();

        TestValidationResult<VerifyElderlyLoginOtpCommand> result =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    DeviceName = string.Empty
                });

        result.ShouldHaveValidationErrorFor(
            command => command.DeviceName);
    }

    [Fact]
    public void VerifyValidator_ShouldRejectUnknownDevicePlatform()
    {
        var validator =
            new VerifyElderlyLoginOtpCommandValidator();

        TestValidationResult<VerifyElderlyLoginOtpCommand> result =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    DevicePlatform = DevicePlatform.Unknown
                });

        result.ShouldHaveValidationErrorFor(
            command => command.DevicePlatform);
    }

    [Fact]
    public void VerifyValidator_ShouldRejectMissingAppVersion()
    {
        var validator =
            new VerifyElderlyLoginOtpCommandValidator();

        TestValidationResult<VerifyElderlyLoginOtpCommand> result =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    AppVersion = string.Empty
                });

        result.ShouldHaveValidationErrorFor(
            command => command.AppVersion);
    }

    private static VerifyElderlyLoginOtpCommand CreateValidCommand()
    {
        return new VerifyElderlyLoginOtpCommand(
            "+201001234567",
            "123456",
            "Ahmed's iPhone",
            DevicePlatform.iOS,
            "1.0.0");
    }
}
