using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Authentication.Tokens;

namespace Ahlena.Modules.Identity.Application.Authentication.Login;

public sealed record LoginResponse(
    UserId UserId,
    AuthAccessType AccessType,
    string AccessToken,
    DateTime AccessTokenExpiresOnUtc,
    string? RefreshToken,
    DateTime? RefreshTokenExpiresOnUtc,
    DeviceSessionId? DeviceSessionId,
    bool EmailVerified,
    bool PhoneVerified,
    VerificationRequestId? EmailVerificationRequestId = null,
    DateTime? EmailVerificationRequestExpiresOnUtc = null,
    VerificationRequestId? PhoneVerificationRequestId = null,
    DateTime? PhoneVerificationRequestExpiresOnUtc = null);