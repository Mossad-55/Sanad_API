using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Authentication.Tokens;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed record StartSocialLoginResponse(
    AuthAccessType? AccessType,
    string? AccessToken,
    DateTime? AccessTokenExpiresOnUtc,
    string? RefreshToken,
    DateTime? RefreshTokenExpiresOnUtc,
    DeviceSessionId? DeviceSessionId,
    string? OpaqueChallenge);