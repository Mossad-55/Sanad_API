using FluentValidation.TestHelper;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Authentication.Sessions;

namespace Ahlena.UnitTests.Identity.Sessions;

public sealed class RevokeSessionCommandValidatorTests
{
    private readonly RevokeSessionCommandValidator
        _validator = new();

    [Fact]
    public void Validate_ShouldAcceptValidCommand()
    {
        RevokeSessionCommand command =
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
        RevokeSessionCommand command =
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
        RevokeSessionCommand command =
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