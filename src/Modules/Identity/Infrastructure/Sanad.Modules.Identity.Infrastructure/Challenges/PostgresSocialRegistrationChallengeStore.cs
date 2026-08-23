using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Identity.Infrastructure.Persistence.Challenges;

namespace Sanad.Modules.Identity.Infrastructure.Challenges;

public sealed class PostgresSocialRegistrationChallengeStore :
    ISocialRegistrationChallengeStore
{
    private readonly IdentityDbContext _dbContext;

    public PostgresSocialRegistrationChallengeStore(
        IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string> CreateAsync(
        SocialRegistrationChallenge challenge,
        CancellationToken cancellationToken)
    {
        string opaqueChallenge =
            CreateOpaqueChallenge();

        SocialRegistrationChallengeRecord record =
            new()
            {
                Id = Guid.CreateVersion7(),
                ChallengeHash = Hash(
                    opaqueChallenge),
                Provider = challenge.Provider,
                ProviderSubject = challenge.ProviderSubject,
                VerifiedEmail = challenge.VerifiedEmail,
                ArabicFullName = challenge.ArabicFullName,
                EnglishFullName = challenge.EnglishFullName,
                AccountType = challenge.AccountType,
                PhoneNumber = challenge.PhoneNumber,
                PhoneVerificationRequestId =
                    challenge.PhoneVerificationRequestId,
                CreatedOnUtc = DateTime.UtcNow,
                ExpiresOnUtc = challenge.ExpiresOnUtc
            };

        _dbContext.SocialRegistrationChallenges.Add(
            record);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return opaqueChallenge;
    }

    public async Task<SocialRegistrationChallenge?> ConsumeAsync(
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
                UPDATE identity.social_registration_challenges
                SET consumed_on_utc = @utcNow
                WHERE challenge_hash = @challengeHash
                  AND consumed_on_utc IS NULL
                  AND expires_on_utc > @utcNow
                RETURNING
                    provider,
                    provider_subject,
                    verified_email,
                    arabic_full_name,
                    english_full_name,
                    account_type,
                    phone_number,
                    phone_verification_request_id,
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

            return new SocialRegistrationChallenge(
                (ExternalLoginProvider)
                    reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                (AccountType)reader.GetInt32(5),
                reader.GetString(6),
                new VerificationRequestId(
                    reader.GetGuid(7)),
                reader.GetDateTime(8));
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