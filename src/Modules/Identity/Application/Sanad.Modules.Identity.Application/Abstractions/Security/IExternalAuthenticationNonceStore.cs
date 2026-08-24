using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Application.Abstractions.Security;

public interface IExternalAuthenticationNonceStore
{
    Task<string> CreateAsync(
        ExternalLoginProvider provider,
        DateTime createdOnUtc,
        DateTime expiresOnUtc,
        CancellationToken cancellationToken);

    Task<bool> ConsumeAsync(
        ExternalLoginProvider provider,
        string nonce,
        DateTime utcNow,
        CancellationToken cancellationToken);
}