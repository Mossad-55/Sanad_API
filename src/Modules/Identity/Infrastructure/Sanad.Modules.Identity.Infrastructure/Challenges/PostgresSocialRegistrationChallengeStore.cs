using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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

    public Task<string> CreateAsync(
        SocialRegistrationChallenge challenge,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        return Task.FromResult(
            opaqueChallenge);
    }

    public async Task<SocialRegistrationChallenge?>
        GetActiveAsync(
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
            Hash(
                opaqueChallenge);

        SocialRegistrationChallengeRecord? record =
            await _dbContext
                .SocialRegistrationChallenges
                .AsTracking()
                .SingleOrDefaultAsync(
                    item =>
                        item.ChallengeHash ==
                            challengeHash &&
                        item.ConsumedOnUtc ==
                            null &&
                        item.ExpiresOnUtc >
                            utcNow,
                    cancellationToken);

        return record is null
            ? null
            : Map(record);
    }

    public async Task<bool> StageConsumeAsync(
        string opaqueChallenge,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                opaqueChallenge))
        {
            return false;
        }

        string challengeHash =
            Hash(
                opaqueChallenge);

        SocialRegistrationChallengeRecord? record =
            await _dbContext
                .SocialRegistrationChallenges
                .AsTracking()
                .SingleOrDefaultAsync(
                    item =>
                        item.ChallengeHash ==
                            challengeHash &&
                        item.ConsumedOnUtc ==
                            null &&
                        item.ExpiresOnUtc >
                            utcNow,
                    cancellationToken);

        if (record is null ||
            record.ConsumedOnUtc is not null)
        {
            return false;
        }

        record.ConsumedOnUtc =
            utcNow;

        return true;
    }

    private static SocialRegistrationChallenge Map(
        SocialRegistrationChallengeRecord record)
    {
        return new SocialRegistrationChallenge(
            record.Provider,
            record.ProviderSubject,
            record.VerifiedEmail,
            record.ArabicFullName,
            record.EnglishFullName,
            record.AccountType,
            record.PhoneNumber,
            record.PhoneVerificationRequestId,
            record.ExpiresOnUtc);
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