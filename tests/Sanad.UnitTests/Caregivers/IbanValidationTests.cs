using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class IbanValidationTests
{
    [Theory]
    [InlineData("GB29NWBK60161331926819", "GB29NWBK60161331926819")]
    [InlineData("gb29 nwbk 6016 1331 9268 19", "GB29NWBK60161331926819")]
    [InlineData("DE89370400440532013000", "DE89370400440532013000")]
    [InlineData("EG380019000500000000263180002", "EG380019000500000000263180002")]
    [InlineData("SA0380000000608010167519", "SA0380000000608010167519")]
    [InlineData("AE070331234567890123456", "AE070331234567890123456")]
    public void TryNormalizeIban_ShouldAcceptRegisteredFormats(
        string iban,
        string expectedNormalized)
    {
        bool valid = IbanValidation.TryNormalizeIban(
            iban,
            out string normalized,
            out IbanInvalidReason reason);

        Assert.True(valid);
        Assert.Equal(expectedNormalized, normalized);
        Assert.Equal(IbanInvalidReason.None, reason);
    }

    [Theory]
    [InlineData("XX29NWBK60161331926819", IbanInvalidReason.UnknownCountry)]
    [InlineData("ZZ380019000500000000263180002", IbanInvalidReason.UnknownCountry)]
    public void TryNormalizeIban_ShouldRejectUnregisteredCountry(
        string iban,
        IbanInvalidReason expectedReason)
    {
        bool valid = IbanValidation.TryNormalizeIban(
            iban,
            out string normalized,
            out IbanInvalidReason reason);

        Assert.False(valid);
        Assert.Equal(string.Empty, normalized);
        Assert.Equal(expectedReason, reason);
    }

    [Theory]
    [InlineData("GB29NWBK6016133192681", IbanInvalidReason.InvalidLength)]
    [InlineData("GB29NWBK601613319268190", IbanInvalidReason.InvalidLength)]
    [InlineData("EG38001900050000000026318002", IbanInvalidReason.InvalidLength)]
    public void TryNormalizeIban_ShouldRejectWrongCountryLength(
        string iban,
        IbanInvalidReason expectedReason)
    {
        bool valid = IbanValidation.TryNormalizeIban(
            iban,
            out _,
            out IbanInvalidReason reason);

        Assert.False(valid);
        Assert.Equal(expectedReason, reason);
    }

    [Theory]
    [InlineData("EG38A019000500000000263180002", IbanInvalidReason.InvalidStructure)]
    [InlineData("SA03A0000000608010167519", IbanInvalidReason.InvalidStructure)]
    public void TryNormalizeIban_ShouldRejectInvalidBbanStructure(
        string iban,
        IbanInvalidReason expectedReason)
    {
        bool valid = IbanValidation.TryNormalizeIban(
            iban,
            out _,
            out IbanInvalidReason reason);

        Assert.False(valid);
        Assert.Equal(expectedReason, reason);
    }

    [Theory]
    [InlineData("GB29NWBK60161331926818", IbanInvalidReason.InvalidChecksum)]
    [InlineData("EG390019000500000000263180002", IbanInvalidReason.InvalidChecksum)]
    public void TryNormalizeIban_ShouldRejectInvalidChecksum(
        string iban,
        IbanInvalidReason expectedReason)
    {
        bool valid = IbanValidation.TryNormalizeIban(
            iban,
            out _,
            out IbanInvalidReason reason);

        Assert.False(valid);
        Assert.Equal(expectedReason, reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalizeIban_ShouldRejectMissingValue(
        string? iban)
    {
        bool valid = IbanValidation.TryNormalizeIban(
            iban,
            out string normalized,
            out _);

        Assert.False(valid);
        Assert.Equal(string.Empty, normalized);
    }
}
