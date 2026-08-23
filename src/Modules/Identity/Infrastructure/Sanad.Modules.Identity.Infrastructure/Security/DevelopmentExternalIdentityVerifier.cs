using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Infrastructure.Security;

public sealed class DevelopmentExternalIdentityVerifier :
    IExternalIdentityVerifier
{
    public Task<VerifiedExternalIdentity?> VerifyAsync(
        ExternalLoginProvider provider,
        string providerCredential,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<VerifiedExternalIdentity?>(
            null);
    }
}