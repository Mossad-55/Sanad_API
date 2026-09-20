using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Subscriptions;

public enum SubscriptionBenefitKey
{
    Chatting = 1,
    Library = 2,
    CommunityForum = 3,
    FamilyActivityTimeline = 4,
    BasicSearch = 5,
    AdvancedSearchFilters = 6,
    MedicalSummaryExportAndSecureSharing = 7,
    PremiumContent = 8
}

public sealed class SubscriptionBenefit : ValueObject
{
    private SubscriptionBenefit(SubscriptionBenefitKey key, bool isIncluded)
    {
        Key = key;
        IsIncluded = isIncluded;
    }

    public SubscriptionBenefitKey Key { get; }
    public bool IsIncluded { get; }

    public static SubscriptionBenefit Create(SubscriptionBenefitKey key, bool isIncluded)
    {
        if (!Enum.IsDefined(key))
            throw new DomainException("Subscription benefit is invalid.");

        return new SubscriptionBenefit(key, isIncluded);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Key;
        yield return IsIncluded;
    }
}
