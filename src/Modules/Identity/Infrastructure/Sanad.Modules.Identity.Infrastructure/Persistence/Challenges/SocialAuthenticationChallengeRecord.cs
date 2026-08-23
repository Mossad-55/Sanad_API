using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Challenges;

internal sealed class SocialAuthenticationChallengeRecord
{
    public Guid Id { get; set; }

    public string ChallengeHash { get; set; } =
        string.Empty;

    public ExternalLoginProvider Provider { get; set; }

    public string ProviderSubject { get; set; } =
        string.Empty;

    public string? VerifiedEmail { get; set; }

    public UserId? ExistingUserId { get; set; }

    public VerificationRequestId?
        LinkVerificationRequestId
    { get; set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime ExpiresOnUtc { get; set; }

    public DateTime? ConsumedOnUtc { get; set; }
}