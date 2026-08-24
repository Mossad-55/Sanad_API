using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;
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

    public Task<string> CreateAsync(
        SocialAuthenticationChallenge challenge,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
                ExpiresOnUtc = challenge.ExpiresOnUtc,
                EmailIsAuthoritative = challenge.EmailIsAuthoritative
            };

        _dbContext.SocialAuthenticationChallenges.Add(
            record);

        return Task.FromResult(
            opaqueChallenge);
    }

    public async Task<SocialAuthenticationChallenge?>
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

        SocialAuthenticationChallengeRecord? record =
            await _dbContext
                .SocialAuthenticationChallenges
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

        SocialAuthenticationChallengeRecord? record =
            await _dbContext
                .SocialAuthenticationChallenges
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

    private static SocialAuthenticationChallenge Map(
        SocialAuthenticationChallengeRecord record)
    {
        return new SocialAuthenticationChallenge(
            record.Provider,
            record.ProviderSubject,
            record.VerifiedEmail,
            record.ExistingUserId,
            record.LinkVerificationRequestId,
            record.ExpiresOnUtc,
            record.EmailIsAuthoritative);
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