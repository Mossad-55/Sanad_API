using FluentValidation.TestHelper;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Users;

namespace Sanad.UnitTests.Identity.Account;

public sealed class UpdateMyProfileCommandValidatorTests
{
    private readonly UpdateMyProfileCommandValidator
        _validator = new();

    [Fact]
    public void Validate_ShouldAcceptPartialUpdate()
    {
        UpdateMyProfileCommand command =
            new(
                UserId.New(),
                ArabicFullName: null,
                EnglishFullName: null,
                Email: "new@example.com",
                PhoneNumber: null);

        TestValidationResult<UpdateMyProfileCommand>
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
        UpdateMyProfileCommand command =
            new(
                UserId.New(),
                ArabicFullName: null,
                EnglishFullName: null,
                Email: email,
                PhoneNumber: null);

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.Email);
    }
}
