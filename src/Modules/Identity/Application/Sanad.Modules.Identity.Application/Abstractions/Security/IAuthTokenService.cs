using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Abstractions.Security;

public interface IAuthTokenService
{
    GeneratedAccessToken GenerateAccessToken(
        User user,
        DateTime utcNow,
        DeviceSessionId? deviceSessionId = null);

    GeneratedAccessToken GenerateRestrictedVerificationToken(
            User user,
            DateTime utcNow);

    GeneratedRefreshToken GenerateRefreshToken(
        DateTime utcNow);

    bool VerifyRefreshToken(
        string providedToken,
        string storedHash);
}