using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Infrastructure.Security;

namespace Sanad.UnitTests.Identity.Infrastructure;

public sealed class ProductionExternalIdentityVerifierTests :
    IDisposable
{
    private const string GoogleAudience =
        "google-server-client-id";

    private const string AppleAudience =
        "com.sanad.mobile";

    private const string GoogleIssuer =
        "https://accounts.google.com";

    private const string AppleIssuer =
        "https://appleid.apple.com";

    private static readonly string RawNonce =
        new(
            'n',
            ExternalAuthenticationNoncePolicy
                .EncodedLength);

    private readonly RSA _rsa;

    private readonly RsaSecurityKey
        _signingKey;

    public ProductionExternalIdentityVerifierTests()
    {
        _rsa =
            RSA.Create(
                2048);

        _signingKey =
            new RsaSecurityKey(
                _rsa)
            {
                KeyId =
                    "test-signing-key"
            };
    }

    [Theory]
    [InlineData(ExternalLoginProvider.Google)]
    [InlineData(ExternalLoginProvider.Apple)]
    public async Task VerifyAsync_ShouldAcceptValidProviderToken(
        ExternalLoginProvider provider)
    {
        var nonceStore =
            new RecordingNonceStore(
                consumeResult: true);

        ProductionExternalIdentityVerifier verifier =
            CreateVerifier(
                nonceStore);

        string email =
            provider ==
                ExternalLoginProvider.Google
                ? "user@gmail.com"
                : "user@privaterelay.appleid.com";

        string token =
            CreateToken(
                provider,
                email,
                emailVerified: true,
                hostedDomain: null,
                audienceOverride: null,
                nonceOverride: null);

        VerifiedExternalIdentity? result =
            await verifier.VerifyAsync(
                provider,
                new ExternalIdentityCredential(
                    token,
                    RawNonce),
                CancellationToken.None);

        Assert.NotNull(
            result);

        Assert.Equal(
            provider,
            result.Provider);

        Assert.Equal(
            "provider-subject",
            result.ProviderSubject);

        Assert.Equal(
            email,
            result.VerifiedEmail);

        Assert.True(
            result.EmailIsAuthoritative);

        Assert.Equal(
            1,
            nonceStore.ConsumeCalls);

        Assert.Equal(
            provider,
            nonceStore.Provider);

        Assert.Equal(
            RawNonce,
            nonceStore.Nonce);
    }

    [Theory]
    [InlineData(InvalidTokenCase.WrongAudience)]
    [InlineData(InvalidTokenCase.WrongNonce)]
    [InlineData(InvalidTokenCase.NonceAlreadyConsumed)]
    public async Task VerifyAsync_ShouldRejectInvalidSecurityBoundary(
        InvalidTokenCase invalidCase)
    {
        var nonceStore =
            new RecordingNonceStore(
                consumeResult:
                    invalidCase !=
                    InvalidTokenCase
                        .NonceAlreadyConsumed);

        ProductionExternalIdentityVerifier verifier =
            CreateVerifier(
                nonceStore);

        string? audienceOverride =
            invalidCase ==
                InvalidTokenCase.WrongAudience
                ? "wrong-audience"
                : null;

        string? nonceOverride =
            invalidCase ==
                InvalidTokenCase.WrongNonce
                ? new string(
                    'x',
                    ExternalAuthenticationNoncePolicy
                        .EncodedLength)
                : null;

        string token =
            CreateToken(
                ExternalLoginProvider.Google,
                "user@gmail.com",
                emailVerified: true,
                hostedDomain: null,
                audienceOverride,
                nonceOverride);

        VerifiedExternalIdentity? result =
            await verifier.VerifyAsync(
                ExternalLoginProvider.Google,
                new ExternalIdentityCredential(
                    token,
                    RawNonce),
                CancellationToken.None);

        Assert.Null(
            result);
    }

    [Fact]
    public async Task VerifyAsync_ShouldRequireSanadVerification_ForThirdPartyGoogleEmail()
    {
        var nonceStore =
            new RecordingNonceStore(
                consumeResult: true);

        ProductionExternalIdentityVerifier verifier =
            CreateVerifier(
                nonceStore);

        string token =
            CreateToken(
                ExternalLoginProvider.Google,
                "user@example.com",
                emailVerified: true,
                hostedDomain: null,
                audienceOverride: null,
                nonceOverride: null);

        VerifiedExternalIdentity? result =
            await verifier.VerifyAsync(
                ExternalLoginProvider.Google,
                new ExternalIdentityCredential(
                    token,
                    RawNonce),
                CancellationToken.None);

        Assert.NotNull(
            result);

        Assert.Equal(
            "user@example.com",
            result.VerifiedEmail);

        Assert.False(
            result.EmailIsAuthoritative);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void OptionsValidator_ShouldRejectEnabledProviderWithoutAudience(
        bool googleEnabled,
        bool appleEnabled)
    {
        var options =
            new ExternalIdentityProviderOptions
            {
                Google =
                    new ExternalIdentityProviderSettings
                    {
                        Enabled =
                            googleEnabled
                    },

                Apple =
                    new ExternalIdentityProviderSettings
                    {
                        Enabled =
                            appleEnabled
                    }
            };

        var validator =
            new ExternalIdentityProviderOptionsValidator();

        ValidateOptionsResult result =
            validator.Validate(
                name: null,
                options);

        Assert.False(
            result.Succeeded);
    }

    private ProductionExternalIdentityVerifier
        CreateVerifier(
            RecordingNonceStore nonceStore)
    {
        var configuration =
            new OpenIdConnectConfiguration();

        configuration.SigningKeys.Add(
            _signingKey);

        return new ProductionExternalIdentityVerifier(
            Options.Create(
                new ExternalIdentityProviderOptions
                {
                    Google =
                        new ExternalIdentityProviderSettings
                        {
                            Enabled =
                                true,

                            Audiences =
                                [GoogleAudience]
                        },

                    Apple =
                        new ExternalIdentityProviderSettings
                        {
                            Enabled =
                                true,

                            Audiences =
                                [AppleAudience]
                        }
                }),
            new StaticConfigurationProvider(
                configuration),
            nonceStore,
            new FixedDateTimeProvider());
    }

    private string CreateToken(
        ExternalLoginProvider provider,
        string email,
        bool emailVerified,
        string? hostedDomain,
        string? audienceOverride,
        string? nonceOverride)
    {
        string issuer =
            provider ==
                ExternalLoginProvider.Google
                ? GoogleIssuer
                : AppleIssuer;

        string audience =
            audienceOverride ??
            (
                provider ==
                    ExternalLoginProvider.Google
                    ? GoogleAudience
                    : AppleAudience);

        string expectedNonce =
            provider ==
                ExternalLoginProvider.Google
                ? RawNonce
                : HashAppleNonce(
                    RawNonce);

        string nonce =
            nonceOverride ??
            expectedNonce;

        List<Claim> claims =
        [
            new(
                JwtRegisteredClaimNames.Sub,
                "provider-subject"),

            new(
                "nonce",
                nonce),

            new(
                "email",
                email),

            new(
                "email_verified",
                emailVerified
                    ? "true"
                    : "false")
        ];

        if (!string.IsNullOrWhiteSpace(
                hostedDomain))
        {
            claims.Add(
                new Claim(
                    "hd",
                    hostedDomain));
        }

        var descriptor =
            new SecurityTokenDescriptor
            {
                Subject =
                    new ClaimsIdentity(
                        claims),

                Issuer =
                    issuer,

                Audience =
                    audience,

                NotBefore =
                    FixedDateTimeProvider
                        .UtcNowValue
                        .AddMinutes(-1),

                IssuedAt =
                    FixedDateTimeProvider
                        .UtcNowValue
                        .AddMinutes(-1),

                Expires =
                    FixedDateTimeProvider
                        .UtcNowValue
                        .AddMinutes(5),

                SigningCredentials =
                    new SigningCredentials(
                        _signingKey,
                        SecurityAlgorithms.RsaSha256)
            };

        var tokenHandler =
            new JsonWebTokenHandler();

        return tokenHandler.CreateToken(
            descriptor);
    }

    private static string HashAppleNonce(
        string nonce)
    {
        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        nonce)))
            .ToLowerInvariant();
    }

    public void Dispose()
    {
        _rsa.Dispose();

        GC.SuppressFinalize(
            this);
    }

    public enum InvalidTokenCase
    {
        WrongAudience = 1,
        WrongNonce = 2,
        NonceAlreadyConsumed = 3
    }

    private sealed class StaticConfigurationProvider :
        IExternalIdentityOpenIdConfigurationProvider
    {
        private readonly OpenIdConnectConfiguration
            _configuration;

        internal StaticConfigurationProvider(
            OpenIdConnectConfiguration configuration)
        {
            _configuration =
                configuration;
        }

        public Task<OpenIdConnectConfiguration> GetAsync(
            ExternalLoginProvider provider,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _configuration);
        }
    }

    private sealed class RecordingNonceStore :
        IExternalAuthenticationNonceStore
    {
        private readonly bool
            _consumeResult;

        internal RecordingNonceStore(
            bool consumeResult)
        {
            _consumeResult =
                consumeResult;
        }

        internal int ConsumeCalls
        {
            get;
            private set;
        }

        internal ExternalLoginProvider Provider
        {
            get;
            private set;
        }

        internal string? Nonce
        {
            get;
            private set;
        }

        public Task<string> CreateAsync(
            ExternalLoginProvider provider,
            DateTime createdOnUtc,
            DateTime expiresOnUtc,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ConsumeAsync(
            ExternalLoginProvider provider,
            string nonce,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            ConsumeCalls++;

            Provider =
                provider;

            Nonce =
                nonce;

            return Task.FromResult(
                _consumeResult);
        }
    }

    private sealed class FixedDateTimeProvider :
        IDateTimeProvider
    {
        internal static readonly DateTime
            UtcNowValue =
                new(
                    2026,
                    8,
                    24,
                    10,
                    0,
                    0,
                    DateTimeKind.Utc);

        public DateTime UtcNow =>
            UtcNowValue;
    }
}