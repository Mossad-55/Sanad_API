using Sanad.Modules.Identity.Application.Authentication.SocialLogin;

namespace Sanad.Modules.Identity.Application.Abstractions.Security;

public interface ISocialRegistrationChallengeStore
{
    Task<string> CreateAsync(
        SocialRegistrationChallenge challenge,
        CancellationToken cancellationToken);

    Task<SocialRegistrationChallenge?> GetActiveAsync(
        string opaqueChallenge,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task<bool> StageConsumeAsync(
        string opaqueChallenge,
        DateTime utcNow,
        CancellationToken cancellationToken);
}