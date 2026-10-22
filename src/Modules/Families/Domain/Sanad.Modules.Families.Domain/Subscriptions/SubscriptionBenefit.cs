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
    public const int MaximumLabelLength = 200;

    private SubscriptionBenefit(
        SubscriptionBenefitKey key,
        bool isIncluded,
        string? arabicLabel,
        string? englishLabel)
    {
        Key = key;
        IsIncluded = isIncluded;
        ArabicLabel = arabicLabel;
        EnglishLabel = englishLabel;
    }

    /// <summary>
    /// The label-free constructor. The authored labels belong to the **plan
    /// version** that owns the benefit; a subscription snapshot stores the
    /// benefit terms only, so its owner does not map the label columns and
    /// needs a constructor it can bind.
    /// </summary>
    private SubscriptionBenefit(
        SubscriptionBenefitKey key,
        bool isIncluded)
    {
        Key = key;
        IsIncluded = isIncluded;
    }

    public SubscriptionBenefitKey Key { get; }
    public bool IsIncluded { get; }

    /// <summary>
    /// The admin-authored Arabic label of this benefit **on the plan version
    /// that owns it**. Absent when no label was authored — it is never
    /// generated, translated or back-filled.
    /// </summary>
    public string? ArabicLabel { get; }

    /// <summary>The admin-authored English label of this benefit (see <see cref="ArabicLabel"/>).</summary>
    public string? EnglishLabel { get; }

    public static SubscriptionBenefit Create(
        SubscriptionBenefitKey key,
        bool isIncluded,
        string? arabicLabel = null,
        string? englishLabel = null)
    {
        if (!Enum.IsDefined(key))
            throw new DomainException("Subscription benefit is invalid.");

        return new SubscriptionBenefit(
            key,
            isIncluded,
            OptionalLabel(arabicLabel, "Arabic benefit label"),
            OptionalLabel(englishLabel, "English benefit label"));
    }

    private static string? OptionalLabel(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string normalized = value.Trim();
        if (normalized.Length > MaximumLabelLength)
            throw new DomainException($"{fieldName} must not exceed {MaximumLabelLength} characters.");

        return normalized;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Key;
        yield return IsIncluded;
        yield return ArabicLabel;
        yield return EnglishLabel;
    }
}
