using FluentValidation.TestHelper;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Authentication.Verification;

namespace Sanad.UnitTests.Identity.Verification;

public sealed class VerifyOtpCommandValidatorTests
{
    private readonly VerifyOtpCommandValidator
        _validator = new();

    [Fact]
    public void Validate_ShouldAcceptValidCommand()
    {
        VerifyOtpCommand command =
            new(
                VerificationRequestId.New(),
                "123456");

        TestValidationResult<VerifyOtpCommand>
            result =
                _validator.TestValidate(
                    command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ShouldRejectEmptyRequestId()
    {
        VerifyOtpCommand command =
            new(
                VerificationRequestId.Empty,
                "123456");

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.VerificationRequestId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12A456")]
    [InlineData("123 56")]
    [InlineData("123456\n")]
    public void Validate_ShouldRejectInvalidCode(
        string? code)
    {
        VerifyOtpCommand command =
            new(
                VerificationRequestId.New(),
                code!);

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.Code);
    }
}