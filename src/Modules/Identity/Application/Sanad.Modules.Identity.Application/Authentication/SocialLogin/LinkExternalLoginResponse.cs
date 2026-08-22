using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed record LinkExternalLoginResponse(
    UserId UserId,
    ExternalLoginProvider Provider,
    DateTime LinkedOnUtc);