using FluentValidation.TestHelper;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Authentication.Sessions;

namespace Ahlena.UnitTests.Identity.Sessions;

public sealed class LogoutCurrentSessionCommandValidatorTests
{
    private readonly LogoutCurrentSessionCommandValidator
        _validator = new();

    [Fact]
    public void Validate_ShouldAcceptValidCommand()
    {
        LogoutCurrentSessionCommand command =
            new(
                DeviceSessionId.New(),
                UserId.New());

        _validator
            .TestValidate(command)
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ShouldRejectEmptySessionId()
    {
        LogoutCurrentSessionCommand command =
            new(
                DeviceSessionId.Empty,
                UserId.New());

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.DeviceSessionId);
    }

    [Fact]
    public void Validate_ShouldRejectEmptyUserId()
    {
        LogoutCurrentSessionCommand command =
            new(
                DeviceSessionId.New(),
                UserId.Empty);

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.CurrentUserId);
    }
}