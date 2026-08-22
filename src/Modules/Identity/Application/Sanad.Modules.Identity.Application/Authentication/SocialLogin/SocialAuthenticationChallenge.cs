using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed record SocialAuthenticationChallenge(
    ExternalLoginProvider Provider,
    string ProviderSubject,
    string? VerifiedEmail,
    UserId? ExistingUserId,
    VerificationRequestId? LinkVerificationRequestId,
    DateTime ExpiresOnUtc);