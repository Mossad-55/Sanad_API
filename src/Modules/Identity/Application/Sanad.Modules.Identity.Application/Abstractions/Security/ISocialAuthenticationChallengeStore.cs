using Sanad.Modules.Identity.Application.Authentication.SocialLogin;

namespace Sanad.Modules.Identity.Application.Abstractions.Security;

public interface ISocialAuthenticationChallengeStore
{
    Task<string> CreateAsync(
        SocialAuthenticationChallenge challenge,
        CancellationToken cancellationToken);

    Task<SocialAuthenticationChallenge?> ConsumeAsync(
        string opaqueChallenge,
        DateTime utcNow,
        CancellationToken cancellationToken);
}