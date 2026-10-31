using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Application.Authentication.Tokens;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Infrastructure.Security;

public sealed class JwtAuthTokenService :
    IAuthTokenService
{
    private static readonly TimeSpan AccessLifetime =
        TimeSpan.FromMinutes(15);

    private static readonly TimeSpan RefreshLifetime =
        TimeSpan.FromDays(30);

    private readonly JwtOptions _options;
    private readonly JwtSecurityTokenHandler _tokenHandler =
        new();

    public JwtAuthTokenService(
        IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public GeneratedAccessToken GenerateAccessToken(
        User user,
        DateTime utcNow,
        DeviceSessionId? deviceSessionId = null)
    {
        return GenerateToken(
            user,
            AuthAccessType.Normal,
            utcNow,
            deviceSessionId);
    }

    public GeneratedAccessToken
        GenerateRestrictedVerificationToken(
            User user,
            DateTime utcNow)
    {
        return GenerateToken(
            user,
            AuthAccessType.RestrictedVerification,
            utcNow);
    }

    public GeneratedRefreshToken GenerateRefreshToken(
        DateTime utcNow)
    {
        byte[] tokenBytes =
            RandomNumberGenerator.GetBytes(32);

        string plainTextToken =
            Base64UrlEncoder.Encode(
                tokenBytes);

        string hash =
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        plainTextToken)));

        return new GeneratedRefreshToken(
            plainTextToken,
            hash,
            utcNow.Add(
                RefreshLifetime));
    }

    public bool VerifyRefreshToken(
        string providedToken,
        string storedHash)
    {
        if (string.IsNullOrWhiteSpace(
                providedToken) ||
            string.IsNullOrWhiteSpace(
                storedHash))
        {
            return false;
        }

        try
        {
            byte[] expectedHash =
                Convert.FromHexString(
                    storedHash);

            byte[] actualHash =
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        providedToken));

            return CryptographicOperations
                .FixedTimeEquals(
                    actualHash,
                    expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private GeneratedAccessToken GenerateToken(
        User user,
        AuthAccessType accessType,
        DateTime utcNow,
        DeviceSessionId? deviceSessionId = null)
    {
        DateTime expiresOnUtc =
            utcNow.Add(
                AccessLifetime);

        SecurityKey signingKey =
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    _options.SigningKey));

        SigningCredentials credentials =
            new(
                signingKey,
                SecurityAlgorithms.HmacSha256);

        List<Claim> claims =
        [
            new(
                JwtRegisteredClaimNames.Sub,
                user.Id.Value.ToString()),

            new(
                AuthClaimNames.AccessType,
                accessType.ToString()),

            new(
                JwtRegisteredClaimNames.Jti,
                Guid.CreateVersion7()
                    .ToString())
        ];

        if (accessType ==
            AuthAccessType.Normal)
        {
            foreach (
                UserAccount account
                in user.Accounts)
            {
                claims.Add(
                    new Claim(
                        AuthClaimNames.AccountType,
                        account.AccountType
                            .ToString()));
            }

            // Session-bound Normal tokens are issued after the device
            // session row exists and carry its id. Restricted tokens never
            // carry a session. A null/empty session preserves the legacy
            // unbound shape for the documented rollout window only.
            if (deviceSessionId.HasValue &&
                deviceSessionId.Value != DeviceSessionId.Empty)
            {
                claims.Add(
                    new Claim(
                        AuthClaimNames.SessionId,
                        deviceSessionId.Value.Value.ToString(
                            "D")));
            }
        }

        SecurityTokenDescriptor descriptor =
            new()
            {
                Subject = new ClaimsIdentity(
                    claims),
                Issuer = _options.Issuer,
                Audience = _options.Audience,
                NotBefore = utcNow,
                IssuedAt = utcNow,
                Expires = expiresOnUtc,
                SigningCredentials = credentials
            };

        SecurityToken token =
            _tokenHandler.CreateToken(
                descriptor);

        return new GeneratedAccessToken(
            _tokenHandler.WriteToken(token),
            expiresOnUtc);
    }
}