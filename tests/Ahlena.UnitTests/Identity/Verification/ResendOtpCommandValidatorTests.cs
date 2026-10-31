using FluentValidation.TestHelper;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Authentication.Verification;

namespace Ahlena.UnitTests.Identity.Verification;

public sealed class ResendOtpCommandValidatorTests
{
    private readonly ResendOtpCommandValidator
        _validator = new();

    [Fact]
    public void Validate_ShouldAcceptValidRequestId()
    {
        ResendOtpCommand command =
            new(
                VerificationRequestId.New());

        TestValidationResult<ResendOtpCommand>
            result =
                _validator.TestValidate(
                    command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ShouldRejectEmptyRequestId()
    {
        ResendOtpCommand command =
            new(
                VerificationRequestId.Empty);

        _validator
            .TestValidate(command)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.VerificationRequestId);
    }
}