using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Challenges;

internal sealed class SocialRegistrationChallengeRecord
{
    public Guid Id { get; set; }

    public string ChallengeHash { get; set; } =
        string.Empty;

    public ExternalLoginProvider Provider { get; set; }

    public string ProviderSubject { get; set; } =
        string.Empty;

    public string VerifiedEmail { get; set; } =
        string.Empty;

    public string ArabicFullName { get; set; } =
        string.Empty;

    public string EnglishFullName { get; set; } =
        string.Empty;

    public AccountType AccountType { get; set; }

    public string PhoneNumber { get; set; } =
        string.Empty;

    public VerificationRequestId PhoneVerificationRequestId { get; set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime ExpiresOnUtc { get; set; }

    public DateTime? ConsumedOnUtc { get; set; }
}