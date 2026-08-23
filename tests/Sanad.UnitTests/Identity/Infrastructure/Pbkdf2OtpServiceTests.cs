using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Infrastructure.Security;

namespace Sanad.UnitTests.Identity.Infrastructure;

public sealed class Pbkdf2OtpServiceTests
{
    [Fact]
    public void Generate_ShouldProduceSixAsciiDigitsAndHashOnlyStorage()
    {
        var otpService = new Pbkdf2OtpService();

        GeneratedOtpCode generatedOtp = otpService.Generate(6);

        Assert.Equal(6, generatedOtp.PlainTextCode.Length);

        Assert.All(
            generatedOtp.PlainTextCode,
            character =>
                Assert.InRange(character, '0', '9'));

        Assert.StartsWith(
            "v1:",
            generatedOtp.Hash);

        Assert.False(string.IsNullOrWhiteSpace(
            generatedOtp.Hash));
    }

    [Fact]
    public void Verify_ShouldReturnTrue_ForGeneratedCode()
    {
        var otpService = new Pbkdf2OtpService();

        GeneratedOtpCode generatedOtp = otpService.Generate(6);

        bool result = otpService.Verify(
            generatedOtp.PlainTextCode,
            generatedOtp.Hash);

        Assert.True(result);
    }

    [Fact]
    public void Verify_ShouldReturnFalse_ForDifferentCode()
    {
        var otpService = new Pbkdf2OtpService();

        GeneratedOtpCode generatedOtp = otpService.Generate(6);

        bool result = otpService.Verify(
            "000000",
            generatedOtp.Hash);

        if (generatedOtp.PlainTextCode == "000000")
        {
            result = otpService.Verify(
                "111111",
                generatedOtp.Hash);
        }

        Assert.False(result);
    }

    [Theory]
    [InlineData("١٢٣٤٥٦")]
    [InlineData("１２３４５６")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12 456")]
    public void Verify_ShouldReturnFalse_ForInvalidCodeFormat(
        string code)
    {
        var otpService = new Pbkdf2OtpService();

        GeneratedOtpCode generatedOtp = otpService.Generate(6);

        bool result = otpService.Verify(
            code,
            generatedOtp.Hash);

        Assert.False(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("v1:not-a-number:salt:key")]
    [InlineData("v1:100000:not-base64:not-base64")]
    public void Verify_ShouldReturnFalse_ForMalformedHash(
        string hash)
    {
        var otpService = new Pbkdf2OtpService();

        bool result = otpService.Verify(
            "123456",
            hash);

        Assert.False(result);
    }

    [Fact]
    public void Generate_ShouldProduceDifferentHashes_ForSameCodeWhenSaltDiffers()
    {
        var otpService = new Pbkdf2OtpService();

        GeneratedOtpCode first = otpService.Generate(6);
        GeneratedOtpCode second = otpService.Generate(6);

        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(7)]
    public void Generate_ShouldRejectNonSixLength(
        int length)
    {
        var otpService = new Pbkdf2OtpService();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => otpService.Generate(length));
    }
}
