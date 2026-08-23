using Sanad.Modules.Identity.Infrastructure.Security;

namespace Sanad.UnitTests.Identity.Infrastructure;

public sealed class JwtOptionsValidatorTests
{
    [Theory]
    [InlineData(null, "audience", "12345678901234567890123456789012")]
    [InlineData("issuer", null, "12345678901234567890123456789012")]
    [InlineData("issuer", "audience", null)]
    [InlineData("issuer", "audience", "short")]
    public void Validate_ShouldRejectInvalidOptions(
        string? issuer,
        string? audience,
        string? signingKey)
    {
        var validator = new JwtOptionsValidator();

        var result = validator.Validate(
            null,
            new JwtOptions
            {
                Issuer = issuer!,
                Audience = audience!,
                SigningKey = signingKey!
            });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Validate_ShouldAcceptValidOptions()
    {
        var validator = new JwtOptionsValidator();

        var result = validator.Validate(
            null,
            new JwtOptions
            {
                Issuer = "sanad-api",
                Audience = "sanad-clients",
                SigningKey = "12345678901234567890123456789012"
            });

        Assert.True(result.Succeeded);
    }
}
