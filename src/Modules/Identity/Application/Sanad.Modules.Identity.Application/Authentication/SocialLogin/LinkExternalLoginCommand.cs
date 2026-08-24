using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed record LinkExternalLoginCommand(
    UserId UserId,
    ExternalLoginProvider Provider,
    string ProviderCredential,
    string Nonce = "")
    : ICommand<LinkExternalLoginResponse>;