using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Authentication.Tokens;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Application.Abstractions.Security;

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