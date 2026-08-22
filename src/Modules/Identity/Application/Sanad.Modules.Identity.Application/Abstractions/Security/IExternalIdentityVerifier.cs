using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Application.Abstractions.Security;

public interface IExternalIdentityVerifier
{
    Task<VerifiedExternalIdentity?> VerifyAsync(
        ExternalLoginProvider provider,
        string providerCredential,
        CancellationToken cancellationToken);
}