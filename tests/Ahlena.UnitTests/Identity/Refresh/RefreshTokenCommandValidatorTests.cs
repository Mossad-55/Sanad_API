using FluentValidation.TestHelper;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Authentication.Refresh;

namespace Ahlena.UnitTests.Identity.Refresh;

public sealed class RefreshTokenCommandValidatorTests
{
    private readonly RefreshTokenCommandValidator
        _validator = new();

    [Fact]
    public void Validate_ShouldAcceptValidCommand()
    {
        RefreshTokenCommand command =
            new(
                DeviceSessionId.New(),
                "refresh-token");

        TestValidationResult<RefreshTokenCommand>
            result =
                _validator.TestValidate(
                    command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ShouldRejectEmptySessionId()
    {
        RefreshTokenCommand command =
            new(
                DeviceSessionId.Empty,
                "refresh-token");

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.DeviceSessionId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ShouldRejectMissingRefreshToken(
        string? refreshToken)
    {
        RefreshTokenCommand command =
            new(
                DeviceSessionId.New(),
                refreshToken!);

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.RefreshToken);
    }
}