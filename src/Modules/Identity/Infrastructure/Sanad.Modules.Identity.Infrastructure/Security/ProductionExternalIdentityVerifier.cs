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

namespace Sanad.Modules.Identity.Infrastructure.Security;

public sealed class ProductionExternalIdentityVerifier :
    IExternalIdentityVerifier
{
    private static readonly TimeSpan MaximumClockSkew =
        TimeSpan.FromSeconds(60);

    private static readonly string[] GoogleIssuers =
    [
        "https://accounts.google.com",
        "accounts.google.com"
    ];

    private const string AppleIssuer =
        "https://appleid.apple.com";

    private readonly ExternalIdentityProviderOptions
        _options;

    private readonly IExternalIdentityOpenIdConfigurationProvider
        _configurationProvider;

    private readonly IExternalAuthenticationNonceStore
        _nonceStore;

    private readonly IDateTimeProvider
        _dateTimeProvider;

    private readonly JsonWebTokenHandler
        _tokenHandler =
            new()
            {
                MapInboundClaims =
                    false
            };

    public ProductionExternalIdentityVerifier(
        IOptions<ExternalIdentityProviderOptions> options,
        IExternalIdentityOpenIdConfigurationProvider
            configurationProvider,
        IExternalAuthenticationNonceStore nonceStore,
        IDateTimeProvider dateTimeProvider)
    {
        _options =
            options.Value;

        _configurationProvider =
            configurationProvider;

        _nonceStore =
            nonceStore;

        _dateTimeProvider =
            dateTimeProvider;
    }

    public async Task<VerifiedExternalIdentity?> VerifyAsync(
        ExternalLoginProvider provider,
        ExternalIdentityCredential credential,
        CancellationToken cancellationToken)
    {
        if (!IsCredentialValid(
                credential))
        {
            return null;
        }

        ExternalIdentityProviderSettings settings =
            GetSettings(
                provider);

        if (!settings.Enabled)
        {
            return null;
        }

        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        try
        {
            OpenIdConnectConfiguration configuration =
                await _configurationProvider.GetAsync(
                    provider,
                    cancellationToken);

            TokenValidationParameters validationParameters =
                CreateValidationParameters(
                    provider,
                    settings,
                    configuration,
                    utcNow);

            TokenValidationResult validationResult =
                await _tokenHandler.ValidateTokenAsync(
                    credential.IdentityToken,
                    validationParameters);

            if (!validationResult.IsValid ||
                validationResult.ClaimsIdentity is null)
            {
                return null;
            }

            ClaimsIdentity identity =
                validationResult.ClaimsIdentity;

            string? subject =
                GetClaim(
                    identity,
                    JwtRegisteredClaimNames.Sub);

            if (!IsValidSubject(
                    subject))
            {
                return null;
            }

            string? tokenNonce =
                GetClaim(
                    identity,
                    "nonce");

            if (!NonceMatches(
                    provider,
                    credential.Nonce,
                    tokenNonce))
            {
                return null;
            }

            string? email =
                GetClaim(
                    identity,
                    "email");

            bool emailVerified =
                ParseBooleanClaim(
                    GetClaim(
                        identity,
                        "email_verified"));

            if (!emailVerified)
            {
                email =
                    null;
            }

            bool emailIsAuthoritative =
                IsEmailAuthoritative(
                    provider,
                    email,
                    emailVerified,
                    GetClaim(
                        identity,
                        "hd"));

            bool nonceConsumed =
                await _nonceStore.ConsumeAsync(
                    provider,
                    credential.Nonce,
                    utcNow,
                    cancellationToken);

            if (!nonceConsumed)
            {
                return null;
            }

            return new VerifiedExternalIdentity(
                provider,
                subject!,
                email,
                emailIsAuthoritative);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private TokenValidationParameters
        CreateValidationParameters(
            ExternalLoginProvider provider,
            ExternalIdentityProviderSettings settings,
            OpenIdConnectConfiguration configuration,
            DateTime utcNow)
    {
        IEnumerable<string> validIssuers =
            provider switch
            {
                ExternalLoginProvider.Google =>
                    GoogleIssuers,

                ExternalLoginProvider.Apple =>
                    [AppleIssuer],

                _ => []
            };

        return new TokenValidationParameters
        {
            ValidateIssuer =
                true,

            ValidIssuers =
                validIssuers,

            ValidateAudience =
                true,

            ValidAudiences =
                settings.Audiences,

            ValidateLifetime =
                true,

            RequireExpirationTime =
                true,

            RequireSignedTokens =
                true,

            ValidateIssuerSigningKey =
                true,

            IssuerSigningKeys =
                configuration.SigningKeys,

            TryAllIssuerSigningKeys =
                true,

            ValidAlgorithms =
                [SecurityAlgorithms.RsaSha256],

            ClockSkew =
                MaximumClockSkew,

            LifetimeValidator =
                (
                    notBefore,
                    expires,
                    _,
                    _) =>
                {
                    if (!expires.HasValue)
                    {
                        return false;
                    }

                    DateTime latestValidNotBefore =
                        utcNow.Add(
                            MaximumClockSkew);

                    DateTime earliestValidExpiration =
                        utcNow.Subtract(
                            MaximumClockSkew);

                    return
                        (!notBefore.HasValue ||
                         notBefore.Value <=
                            latestValidNotBefore) &&
                        expires.Value >
                            earliestValidExpiration;
                }
        };
    }

    private ExternalIdentityProviderSettings GetSettings(
        ExternalLoginProvider provider)
    {
        return provider switch
        {
            ExternalLoginProvider.Google =>
                _options.Google,

            ExternalLoginProvider.Apple =>
                _options.Apple,

            _ => new ExternalIdentityProviderSettings()
        };
    }

    private static bool IsCredentialValid(
        ExternalIdentityCredential credential)
    {
        return !string.IsNullOrWhiteSpace(
                   credential.IdentityToken) &&
               !string.IsNullOrWhiteSpace(
                   credential.Nonce) &&
               credential.Nonce.Length ==
                   ExternalAuthenticationNoncePolicy
                       .EncodedLength;
    }

    private static bool IsValidSubject(
        string? subject)
    {
        return !string.IsNullOrWhiteSpace(
                   subject) &&
               subject.Length <=
                   UserExternalLogin
                       .MaximumProviderSubjectLength;
    }

    private static bool NonceMatches(
        ExternalLoginProvider provider,
        string rawNonce,
        string? tokenNonce)
    {
        if (string.IsNullOrWhiteSpace(
                tokenNonce))
        {
            return false;
        }

        string expectedNonce =
            provider switch
            {
                ExternalLoginProvider.Google =>
                    rawNonce,

                ExternalLoginProvider.Apple =>
                    Convert.ToHexString(
                            SHA256.HashData(
                                Encoding.UTF8.GetBytes(
                                    rawNonce)))
                        .ToLowerInvariant(),

                _ =>
                    string.Empty
            };

        byte[] expectedBytes =
            Encoding.UTF8.GetBytes(
                expectedNonce);

        byte[] actualBytes =
            Encoding.UTF8.GetBytes(
                tokenNonce);

        return expectedBytes.Length ==
                   actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(
                   expectedBytes,
                   actualBytes);
    }

    private static bool IsEmailAuthoritative(
        ExternalLoginProvider provider,
        string? email,
        bool emailVerified,
        string? hostedDomain)
    {
        if (!emailVerified ||
            string.IsNullOrWhiteSpace(
                email))
        {
            return false;
        }

        return provider switch
        {
            ExternalLoginProvider.Apple =>
                true,

            ExternalLoginProvider.Google =>
                email.EndsWith(
                    "@gmail.com",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(
                    hostedDomain),

            _ =>
                false
        };
    }

    private static string? GetClaim(
        ClaimsIdentity identity,
        string claimType)
    {
        return identity.FindFirst(
            claimType)?.Value;
    }

    private static bool ParseBooleanClaim(
        string? value)
    {
        return string.Equals(
                   value,
                   "true",
                   StringComparison.OrdinalIgnoreCase) ||
               value == "1";
    }
}