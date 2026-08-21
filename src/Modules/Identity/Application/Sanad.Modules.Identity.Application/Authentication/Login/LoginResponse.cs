using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Authentication.Tokens;

namespace Sanad.Modules.Identity.Application.Authentication.Login;

public sealed record LoginResponse(
    UserId UserId,
    AuthAccessType AccessType,
    string AccessToken,
    DateTime AccessTokenExpiresOnUtc,
    string? RefreshToken,
    DateTime? RefreshTokenExpiresOnUtc,
    DeviceSessionId? DeviceSessionId,
    bool EmailVerified,
    bool PhoneVerified);