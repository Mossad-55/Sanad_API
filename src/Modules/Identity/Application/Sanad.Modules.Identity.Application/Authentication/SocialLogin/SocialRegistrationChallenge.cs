using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed record SocialRegistrationChallenge(
    ExternalLoginProvider Provider,
    string ProviderSubject,
    string VerifiedEmail,
    string ArabicFullName,
    string EnglishFullName,
    AccountType AccountType,
    string PhoneNumber,
    VerificationRequestId PhoneVerificationRequestId,
    DateTime ExpiresOnUtc);