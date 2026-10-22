using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Domain.Subscriptions;

public sealed class SubscriptionPlanVersion : Entity<Guid>
{
    public const int MaximumNameLength = 200;
    public const int MaximumDescriptionLength = 1000;

    private readonly List<SubscriptionBenefit> _benefits = [];

    private SubscriptionPlanVersion()
    {
    }

    private SubscriptionPlanVersion(
        Guid id,
        SubscriptionPlan plan,
        bool isPublished,
        bool isAvailableForNewSales,
        DateTime createdOnUtc,
        DateTime? publishedOnUtc,
        int? paymobSubscriptionPlanId,
        string? arabicName,
        string? englishName,
        string? arabicDescription,
        string? englishDescription)
        : base(id)
    {
        Key = plan.Key;
        Version = plan.Version;
        Price = plan.Price;
        Cycle = plan.Cycle;
        Currency = plan.Currency;
        MemberLimitKind = plan.MemberLimit.Kind;
        MemberLimitValue = plan.MemberLimit.Value;
        MonthlyBookingLimitKind = plan.MonthlyBookingLimit.Kind;
        MonthlyBookingLimitValue = plan.MonthlyBookingLimit.Value;
        Rollover = plan.Rollover;
        _benefits.AddRange(plan.Benefits);
        IsPublished = isPublished;
        IsAvailableForNewSales = isAvailableForNewSales;
        CreatedOnUtc = createdOnUtc;
        PublishedOnUtc = publishedOnUtc;
        PaymobSubscriptionPlanId = paymobSubscriptionPlanId;
        ArabicName = arabicName;
        EnglishName = englishName;
        ArabicDescription = arabicDescription;
        EnglishDescription = englishDescription;
    }

    public string Key { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public decimal Price { get; private set; }
    public SubscriptionCycle Cycle { get; private set; }
    public string Currency { get; private set; } = SubscriptionPlan.DefaultCurrency;
    public SubscriptionLimitKind MemberLimitKind { get; private set; }
    public int? MemberLimitValue { get; private set; }
    public SubscriptionLimitKind MonthlyBookingLimitKind { get; private set; }
    public int? MonthlyBookingLimitValue { get; private set; }
    public SubscriptionRollover Rollover { get; private set; }
    public bool IsPublished { get; private set; }
    public bool IsAvailableForNewSales { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? PublishedOnUtc { get; private set; }
    public int? PaymobSubscriptionPlanId { get; private set; }

    /// <summary>
    /// The admin-authored Arabic name of **this version**. Absent on versions
    /// created before the copy existed and on versions whose author supplied
    /// none — it is never generated, translated or back-filled.
    /// </summary>
    public string? ArabicName { get; private set; }

    /// <summary>The admin-authored English name of this version (see <see cref="ArabicName"/>).</summary>
    public string? EnglishName { get; private set; }

    /// <summary>The admin-authored Arabic description of this version (see <see cref="ArabicName"/>).</summary>
    public string? ArabicDescription { get; private set; }

    /// <summary>The admin-authored English description of this version (see <see cref="ArabicName"/>).</summary>
    public string? EnglishDescription { get; private set; }

    public IReadOnlyCollection<SubscriptionBenefit> Benefits => _benefits.AsReadOnly();

    public static SubscriptionPlanVersion Create(
        SubscriptionPlan plan,
        bool isPublished = false,
        bool isAvailableForNewSales = true,
        DateTime? createdOnUtc = null,
        DateTime? publishedOnUtc = null,
        int? paymobSubscriptionPlanId = null,
        string? arabicName = null,
        string? englishName = null,
        string? arabicDescription = null,
        string? englishDescription = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (isPublished && publishedOnUtc is null)
            throw new DomainException("A published subscription plan requires a published timestamp.");

        if (!isPublished && publishedOnUtc is not null)
            throw new DomainException("An unpublished subscription plan cannot have a published timestamp.");

        if (paymobSubscriptionPlanId is <= 0)
            throw new DomainException("Paymob subscription plan id must be positive.");

        return new SubscriptionPlanVersion(
            Guid.CreateVersion7(),
            plan,
            isPublished,
            isAvailableForNewSales,
            createdOnUtc ?? DateTime.UtcNow,
            publishedOnUtc,
            paymobSubscriptionPlanId,
            OptionalCopy(arabicName, MaximumNameLength, "Arabic name"),
            OptionalCopy(englishName, MaximumNameLength, "English name"),
            OptionalCopy(arabicDescription, MaximumDescriptionLength, "Arabic description"),
            OptionalCopy(englishDescription, MaximumDescriptionLength, "English description"));
    }

    /// <summary>
    /// Normalizes one authored copy value: trimmed, empty becomes absent
    /// (null) and an over-long value is rejected. No copy is ever generated.
    /// </summary>
    private static string? OptionalCopy(string? value, int maximumLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new DomainException($"{fieldName} must not exceed {maximumLength} characters.");

        return normalized;
    }

    public void Publish(DateTime publishedOnUtc)
    {
        if (IsPublished || PublishedOnUtc is not null)
            throw new DomainException("Subscription plan is already published.");

        if (publishedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Publication timestamp must be UTC.");

        IsPublished = true;
        PublishedOnUtc = publishedOnUtc;
    }

    public void RetireFromNewSales()
    {
        IsAvailableForNewSales = false;
    }

}
