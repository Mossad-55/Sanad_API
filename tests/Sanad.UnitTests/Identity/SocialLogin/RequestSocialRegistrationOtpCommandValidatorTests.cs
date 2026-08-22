using FluentValidation.TestHelper;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.Identity.SocialLogin;

public sealed class RequestSocialRegistrationOtpCommandValidatorTests
{
    [Theory]
    [InlineData(AccountType.Family)]
    [InlineData(AccountType.MedicalCaregiver)]
    [InlineData(AccountType.CompanionCaregiver)]
    public void Validate_ShouldAcceptSupportedAccountType(
        AccountType accountType)
    {
        var validator =
            new RequestSocialRegistrationOtpCommandValidator();

        TestValidationResult<
            RequestSocialRegistrationOtpCommand> result =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    AccountType = accountType
                });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(AccountType.Elderly)]
    [InlineData((AccountType)999)]
    public void Validate_ShouldRejectUnsupportedAccountType(
        AccountType accountType)
    {
        var validator =
            new RequestSocialRegistrationOtpCommandValidator();

        TestValidationResult<
            RequestSocialRegistrationOtpCommand> result =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    AccountType = accountType
                });

        result.ShouldHaveValidationErrorFor(
            command => command.AccountType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    public void Validate_ShouldRejectMissingOrInvalidNames(
        string value)
    {
        var validator =
            new RequestSocialRegistrationOtpCommandValidator();

        TestValidationResult<
            RequestSocialRegistrationOtpCommand> arabicResult =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    ArabicFullName = value
                });

        arabicResult.ShouldHaveValidationErrorFor(
            command => command.ArabicFullName);

        TestValidationResult<
            RequestSocialRegistrationOtpCommand> englishResult =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    EnglishFullName = value
                });

        englishResult.ShouldHaveValidationErrorFor(
            command => command.EnglishFullName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("201001234567")]
    [InlineData("+20 1001234567")]
    [InlineData("+001001234567")]
    public void Validate_ShouldRejectInvalidPhoneNumber(
        string phoneNumber)
    {
        var validator =
            new RequestSocialRegistrationOtpCommandValidator();

        TestValidationResult<
            RequestSocialRegistrationOtpCommand> result =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    PhoneNumber = phoneNumber
                });

        result.ShouldHaveValidationErrorFor(
            command => command.PhoneNumber);
    }

    [Fact]
    public void Validate_ShouldRejectMissingOpaqueChallenge()
    {
        var validator =
            new RequestSocialRegistrationOtpCommandValidator();

        TestValidationResult<
            RequestSocialRegistrationOtpCommand> result =
            validator.TestValidate(
                CreateValidCommand() with
                {
                    OpaqueChallenge = string.Empty
                });

        result.ShouldHaveValidationErrorFor(
            command => command.OpaqueChallenge);
    }

    private static RequestSocialRegistrationOtpCommand
        CreateValidCommand()
    {
        return new RequestSocialRegistrationOtpCommand(
            "opaque-social-challenge",
            "محمد أحمد",
            "Mohamed Ahmed",
            AccountType.Family,
            "+201001234567");
    }
}