using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Security;

namespace Sanad.UnitTests.Identity.Infrastructure;

public sealed class JwtAuthTokenServiceTests
{
    [Fact]
    public void GenerateAccessToken_ShouldCreateNormalTokenWithAccountClaims()
    {
        JwtAuthTokenService service = CreateService();
        User user = CreateUser();
        user.AddAccount(AccountType.Family);
        user.AddAccount(AccountType.MedicalCaregiver);
        DateTime utcNow = CreateUtcNow();

        GeneratedAccessToken generated = service.GenerateAccessToken(user, utcNow);
        JwtSecurityToken token = new JwtSecurityTokenHandler().ReadJwtToken(generated.PlainTextToken);

        Assert.Equal(utcNow.AddMinutes(15), generated.ExpiresOnUtc);
        Assert.Equal("sanad-api", token.Issuer);
        Assert.Contains("sanad-clients", token.Audiences);
        Assert.Equal(user.Id.Value.ToString(), token.Subject);
        Assert.Equal("Normal", token.Claims.Single(claim => claim.Type == "access_type").Value);
        Assert.Equal(2, token.Claims.Count(claim => claim.Type == "account_type"));
        Assert.NotEmpty(token.Id);
    }

    [Fact]
    public void GenerateRestrictedVerificationToken_ShouldExcludeAccountClaims()
    {
        JwtAuthTokenService service = CreateService();
        User user = CreateUser();
        user.AddAccount(AccountType.Family);
        DateTime utcNow = CreateUtcNow();

        GeneratedAccessToken generated = service.GenerateRestrictedVerificationToken(user, utcNow);
        JwtSecurityToken token = new JwtSecurityTokenHandler().ReadJwtToken(generated.PlainTextToken);

        Assert.Equal(utcNow.AddMinutes(15), generated.ExpiresOnUtc);
        Assert.Equal("RestrictedVerification", token.Claims.Single(claim => claim.Type == "access_type").Value);
        Assert.DoesNotContain(token.Claims, claim => claim.Type == "account_type");
    }

    [Fact]
    public void RefreshToken_ShouldRoundTripAndExpireInThirtyDays()
    {
        JwtAuthTokenService service = CreateService();
        DateTime utcNow = CreateUtcNow();

        GeneratedRefreshToken token = service.GenerateRefreshToken(utcNow);

        Assert.Equal(utcNow.AddDays(30), token.ExpiresOnUtc);
        Assert.NotEqual(token.PlainTextToken, token.Hash);
        Assert.True(service.VerifyRefreshToken(token.PlainTextToken, token.Hash));
        Assert.False(service.VerifyRefreshToken("different-token", token.Hash));
        Assert.False(service.VerifyRefreshToken(token.PlainTextToken, "not-a-hex-hash"));
    }

    [Fact]
    public void RefreshToken_ShouldGenerateDistinctTokens()
    {
        JwtAuthTokenService service = CreateService();
        DateTime utcNow = CreateUtcNow();

        GeneratedRefreshToken first = service.GenerateRefreshToken(utcNow);
        GeneratedRefreshToken second = service.GenerateRefreshToken(utcNow);

        Assert.NotEqual(first.PlainTextToken, second.PlainTextToken);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    private static JwtAuthTokenService CreateService()
    {
        return new JwtAuthTokenService(
            Options.Create(
                new JwtOptions
                {
                    Issuer = "sanad-api",
                    Audience = "sanad-clients",
                    SigningKey = "12345678901234567890123456789012"
                }));
    }

    private static User CreateUser()
    {
        return User.Create(
            FullName.Create("محمد أحمد"),
            FullName.Create("Mohamed Ahmed"),
            Email.Create("mohamed@example.com"),
            PhoneNumber.Create("+201001234567"));
    }

    private static DateTime CreateUtcNow()
    {
        return new DateTime(2026, 8, 23, 10, 0, 0, DateTimeKind.Utc);
    }
}
