using FluentValidation.TestHelper;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Users;

namespace Ahlena.UnitTests.Identity.Account;

public sealed class UpdateMyUiLanguageCommandValidatorTests
{
    private readonly UpdateMyUiLanguageCommandValidator
        _validator = new();

    [Fact]
    public void Validator_ShouldAcceptDefinedLanguage_AndRejectUndefined()
    {
        UpdateMyUiLanguageCommand validCommand =
            new(
                UserId.New(),
                UiLanguage.Arabic);

        _validator
            .TestValidate(validCommand)
            .ShouldNotHaveAnyValidationErrors();

        UpdateMyUiLanguageCommand invalidCommand =
            new(
                UserId.New(),
                (UiLanguage)99);

        _validator
            .TestValidate(invalidCommand)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.UiLanguage);
    }
}
