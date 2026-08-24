using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed record RequestExternalAuthenticationNonceCommand(
    ExternalLoginProvider Provider)
    : ICommand<RequestExternalAuthenticationNonceResponse>;