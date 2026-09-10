using FluentValidation.TestHelper;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Users;

namespace Sanad.UnitTests.Identity.Account;

public sealed class UpdateMyNotificationPreferencesCommandValidatorTests
{
    private readonly UpdateMyNotificationPreferencesCommandValidator
        _validator = new();

    [Fact]
    public void Validator_ShouldAcceptAnyBoolCombination_AndRejectEmptyUserId()
    {
        UpdateMyNotificationPreferencesCommand allTrue =
            new(
                UserId.New(),
                true,
                true,
                true,
                true);

        _validator
            .TestValidate(allTrue)
            .ShouldNotHaveAnyValidationErrors();

        UpdateMyNotificationPreferencesCommand allFalse =
            new(
                UserId.New(),
                false,
                false,
                false,
                false);

        _validator
            .TestValidate(allFalse)
            .ShouldNotHaveAnyValidationErrors();

        UpdateMyNotificationPreferencesCommand mixed =
            new(
                UserId.New(),
                true,
                false,
                true,
                false);

        _validator
            .TestValidate(mixed)
            .ShouldNotHaveAnyValidationErrors();

        UpdateMyNotificationPreferencesCommand emptyUserId =
            new(
                UserId.Empty,
                true,
                true,
                true,
                true);

        _validator
            .TestValidate(emptyUserId)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.CurrentUserId);
    }
}
