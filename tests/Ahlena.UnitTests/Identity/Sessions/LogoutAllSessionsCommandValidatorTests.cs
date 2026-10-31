using FluentValidation.TestHelper;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Authentication.Sessions;

namespace Ahlena.UnitTests.Identity.Sessions;

public sealed class LogoutAllSessionsCommandValidatorTests
{
    private readonly LogoutAllSessionsCommandValidator
        _validator = new();

    [Fact]
    public void Validate_ShouldAcceptValidCommand()
    {
        LogoutAllSessionsCommand command =
            new(
                UserId.New());

        _validator
            .TestValidate(command)
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ShouldRejectEmptyUserId()
    {
        LogoutAllSessionsCommand command =
            new(
                UserId.Empty);

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.CurrentUserId);
    }
}