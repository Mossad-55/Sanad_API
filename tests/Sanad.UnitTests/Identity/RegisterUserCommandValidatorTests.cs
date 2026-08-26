using FluentValidation.TestHelper;
using Sanad.Modules.Identity.Application.Authentication.Registration;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.Identity;

public sealed class RegisterUserCommandValidatorTests
{
    private readonly RegisterUserCommandValidator
        _validator = new();

    [Theory]
    [InlineData(AccountType.Family)]
    [InlineData(AccountType.MedicalCaregiver)]
    [InlineData(AccountType.CompanionCaregiver)]
    public void Validate_ShouldAcceptValidCommand(
        AccountType accountType)
    {
        RegisterUserCommand command =
            CreateValidCommand(
                accountType);

        TestValidationResult<RegisterUserCommand>
            result =
                _validator.TestValidate(
                    command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(AccountType.Elderly)]
    [InlineData(AccountType.SuperAdmin)]
    [InlineData(AccountType.ContentAdmin)]
    [InlineData(AccountType.SupportAdmin)]
    [InlineData((AccountType)999)]
    public void Validate_ShouldRejectUnsupportedAccountType(
        AccountType accountType)
    {
        RegisterUserCommand command =
            CreateValidCommand(
                accountType);

        TestValidationResult<RegisterUserCommand>
            result =
                _validator.TestValidate(
                    command);

        result.ShouldHaveValidationErrorFor(
            command =>
                command.AccountType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    public void Validate_ShouldRejectInvalidArabicName(
        string arabicName)
    {
        RegisterUserCommand command =
            CreateValidCommand() with
            {
                ArabicFullName = arabicName
            };

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.ArabicFullName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid-email")]
    public void Validate_ShouldRejectInvalidEmail(
        string email)
    {
        RegisterUserCommand command =
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

    [Theory]
    [InlineData("")]
    [InlineData("01001234567")]
    [InlineData("+0201001234567")]
    [InlineData("+2٠١٠٠١٢٣٤٥٦٧")]
    public void Validate_ShouldRejectInvalidPhone(
        string phoneNumber)
    {
        RegisterUserCommand command =
            CreateValidCommand() with
            {
                PhoneNumber = phoneNumber
            };

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.PhoneNumber);
    }

    [Theory]
    [InlineData("Short1")]
    [InlineData("lowercase123")]
    [InlineData("UPPERCASE123")]
    [InlineData("NoNumberPassword")]
    public void Validate_ShouldRejectInvalidPassword(
        string password)
    {
        RegisterUserCommand command =
            CreateValidCommand() with
            {
                Password = password
            };

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.Password);
    }

    [Fact]
    public void Validate_ShouldAllowMissingAvatar()
    {
        RegisterUserCommand command =
            CreateValidCommand() with
            {
                AvatarUrl = null
            };

        _validator
            .TestValidate(command)
            .ShouldNotHaveValidationErrorFor(
                value =>
                    value.AvatarUrl);
    }

    [Fact]
    public void Validate_ShouldRejectLongAvatarUrl()
    {
        RegisterUserCommand command =
            CreateValidCommand() with
            {
                AvatarUrl = new string(
                    'A',
                    RegisterUserCommandValidator
                        .MaximumAvatarUrlLength + 1)
            };

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.AvatarUrl);
    }

    private static RegisterUserCommand CreateValidCommand(
        AccountType accountType =
            AccountType.Family)
    {
        return new RegisterUserCommand(
            ArabicFullName: "محمد أحمد",
            EnglishFullName: "Mohamed Ahmed",
            Email: "mohamed@example.com",
            PhoneNumber: "+201001234567",
            Password: "StrongPass123",
            AccountType: accountType,
            AvatarUrl: null);
    }
}