using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Infrastructure.Security;

public interface IExternalIdentityOpenIdConfigurationProvider
{
    Task<OpenIdConnectConfiguration> GetAsync(
        ExternalLoginProvider provider,
        CancellationToken cancellationToken);
}