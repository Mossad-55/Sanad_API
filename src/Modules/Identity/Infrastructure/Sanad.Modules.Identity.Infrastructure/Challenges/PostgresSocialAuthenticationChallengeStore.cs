using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Identity.Infrastructure.Persistence.Challenges;

namespace Sanad.Modules.Identity.Infrastructure.Challenges;

public sealed class PostgresSocialAuthenticationChallengeStore :
    ISocialAuthenticationChallengeStore
{
    private readonly IdentityDbContext _dbContext;

    public PostgresSocialAuthenticationChallengeStore(
        IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string> CreateAsync(
        SocialAuthenticationChallenge challenge,
        CancellationToken cancellationToken)
    {
        string opaqueChallenge =
            CreateOpaqueChallenge();

        SocialAuthenticationChallengeRecord record =
            new()
            {
                Id = Guid.CreateVersion7(),
                ChallengeHash = Hash(
                    opaqueChallenge),
                Provider = challenge.Provider,
                ProviderSubject = challenge.ProviderSubject,
                VerifiedEmail = challenge.VerifiedEmail,
                ExistingUserId = challenge.ExistingUserId,
                LinkVerificationRequestId =
                    challenge.LinkVerificationRequestId,
                CreatedOnUtc = DateTime.UtcNow,
                ExpiresOnUtc = challenge.ExpiresOnUtc
            };

        _dbContext.SocialAuthenticationChallenges.Add(
            record);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return opaqueChallenge;
    }

    public async Task<SocialAuthenticationChallenge?> ConsumeAsync(
        string opaqueChallenge,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
            opaqueChallenge))
        {
            return null;
        }

        string challengeHash =
            Hash(opaqueChallenge);

        NpgsqlConnection connection =
            (NpgsqlConnection)_dbContext.Database
                .GetDbConnection();

        await connection.OpenAsync(
            cancellationToken);

        try
        {
            await using NpgsqlCommand command =
                connection.CreateCommand();

            command.CommandText = """
                UPDATE identity.social_authentication_challenges
                SET consumed_on_utc = @utcNow
                WHERE challenge_hash = @challengeHash
                  AND consumed_on_utc IS NULL
                  AND expires_on_utc > @utcNow
                RETURNING
                    provider,
                    provider_subject,
                    verified_email,
                    existing_user_id,
                    link_verification_request_id,
                    expires_on_utc;
                """;

            command.Parameters.AddWithValue(
                "utcNow",
                utcNow);

            command.Parameters.AddWithValue(
                "challengeHash",
                challengeHash);

            await using NpgsqlDataReader reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            if (!await reader.ReadAsync(
                cancellationToken))
            {
                return null;
            }

            return new SocialAuthenticationChallenge(
                (ExternalLoginProvider)
                    reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2)
                    ? null
                    : reader.GetString(2),
                reader.IsDBNull(3)
                    ? null
                    : new UserId(
                        reader.GetGuid(3)),
                reader.IsDBNull(4)
                    ? null
                    : new VerificationRequestId(
                        reader.GetGuid(4)),
                reader.GetDateTime(5));
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static string CreateOpaqueChallenge()
    {
        return Base64UrlEncoder.Encode(
            RandomNumberGenerator.GetBytes(32));
    }

    private static string Hash(
        string opaqueChallenge)
    {
        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    opaqueChallenge)));
    }
}