using FluentValidation.Results;
using Sanad.Modules.Cms.Application.Help;
using Sanad.Modules.Cms.Domain.Help;

namespace Sanad.UnitTests.Cms;

/// <summary>
/// Support contact validation: a null value means "clear this field" and is
/// accepted, while a malformed populated value is still rejected on its own
/// field so the admin surface answers 400 as before.
/// </summary>
public sealed class SupportContactValidatorTests
{
    [Fact]
    public async Task Validator_AcceptsBothValues()
    {
        ValidationResult result = await new UpsertSupportContactCommandValidator()
            .ValidateAsync(
                new UpsertSupportContactCommand(
                    "+201000000001",
                    "support@sanad.example"));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Validator_AcceptsBothNull()
    {
        ValidationResult result = await new UpsertSupportContactCommandValidator()
            .ValidateAsync(new UpsertSupportContactCommand(null, null));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(null, "support@sanad.example")]
    [InlineData("+201000000001", null)]
    public async Task Validator_AcceptsEachValueIndependently(
        string? phone,
        string? email)
    {
        ValidationResult result = await new UpsertSupportContactCommandValidator()
            .ValidateAsync(new UpsertSupportContactCommand(phone, email));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("01000000001")]
    [InlineData("+20 100 000 0001")]
    [InlineData("not-a-phone")]
    public async Task Validator_RejectsAMalformedPhoneOnThePhoneField(
        string phone)
    {
        ValidationResult result = await new UpsertSupportContactCommandValidator()
            .ValidateAsync(
                new UpsertSupportContactCommand(phone, null));

        Assert.False(result.IsValid);

        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Equal("SupportPhone", failure.PropertyName);
        Assert.Contains("E.164", failure.ErrorMessage);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("support@")]
    public async Task Validator_RejectsAMalformedEmailOnTheEmailField(
        string email)
    {
        ValidationResult result = await new UpsertSupportContactCommandValidator()
            .ValidateAsync(
                new UpsertSupportContactCommand(null, email));

        Assert.False(result.IsValid);

        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Equal("SupportEmail", failure.PropertyName);
    }

    [Fact]
    public async Task Validator_RejectsValuesOverTheStoredLengthLimits()
    {
        string longPhone = "+" + new string('1', SupportContact.MaximumPhoneLength);
        string longEmail = new string('a', SupportContact.MaximumEmailLength)
            + "@sanad.example";

        ValidationResult phoneResult = await new UpsertSupportContactCommandValidator()
            .ValidateAsync(new UpsertSupportContactCommand(longPhone, null));
        ValidationResult emailResult = await new UpsertSupportContactCommandValidator()
            .ValidateAsync(new UpsertSupportContactCommand(null, longEmail));

        Assert.False(phoneResult.IsValid);
        Assert.False(emailResult.IsValid);
        Assert.Contains(
            phoneResult.Errors,
            failure => failure.PropertyName == "SupportPhone");
        Assert.Contains(
            emailResult.Errors,
            failure => failure.PropertyName == "SupportEmail");
    }
}
