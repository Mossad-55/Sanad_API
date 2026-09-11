using FluentValidation.TestHelper;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Support;

namespace Sanad.UnitTests.Identity.Support;

public sealed class SubmitSupportRequestCommandValidatorTests
{
    private readonly SubmitSupportRequestCommandValidator
        _validator = new();

    [Fact]
    public void Validator_ShouldAcceptValidRequest()
    {
        SubmitSupportRequestCommand command = new(
            UserId.New(),
            "Help needed",
            "I need assistance with my account.");

        _validator
            .TestValidate(command)
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validator_ShouldRejectBlankSubjectAndMessage()
    {
        SubmitSupportRequestCommand blankSubject = new(
            UserId.New(),
            "",
            "I need assistance with my account.");

        _validator
            .TestValidate(blankSubject)
            .ShouldHaveValidationErrorFor(
                value => value.Subject);

        SubmitSupportRequestCommand blankMessage = new(
            UserId.New(),
            "Help needed",
            "");

        _validator
            .TestValidate(blankMessage)
            .ShouldHaveValidationErrorFor(
                value => value.Message);
    }

    [Fact]
    public void Validator_ShouldRejectOverMaxSubjectAndMessage()
    {
        SubmitSupportRequestCommand overMaxSubject = new(
            UserId.New(),
            new string('S', 151),
            "I need assistance with my account.");

        _validator
            .TestValidate(overMaxSubject)
            .ShouldHaveValidationErrorFor(
                value => value.Subject);

        SubmitSupportRequestCommand overMaxMessage = new(
            UserId.New(),
            "Help needed",
            new string('M', 2001));

        _validator
            .TestValidate(overMaxMessage)
            .ShouldHaveValidationErrorFor(
                value => value.Message);
    }
}
