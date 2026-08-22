using Sanad.Modules.Identity.Application.Authentication.SocialLogin;

namespace Sanad.Modules.Identity.Application.Abstractions.Security;

public interface ISocialRegistrationChallengeStore
{
    Task<string> CreateAsync(
        SocialRegistrationChallenge challenge,
        CancellationToken cancellationToken);

    Task<SocialRegistrationChallenge?> ConsumeAsync(
        string opaqueChallenge,
        DateTime utcNow,
        CancellationToken cancellationToken);
}