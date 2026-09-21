using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Domain.Subscriptions;

public sealed class SubscriptionPlanVersion : Entity<Guid>
{
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
        DateTime? publishedOnUtc)
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
    public IReadOnlyCollection<SubscriptionBenefit> Benefits => _benefits.AsReadOnly();

    public static SubscriptionPlanVersion Create(
        SubscriptionPlan plan,
        bool isPublished = false,
        bool isAvailableForNewSales = true,
        DateTime? createdOnUtc = null,
        DateTime? publishedOnUtc = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (isPublished && publishedOnUtc is null)
            throw new DomainException("A published subscription plan requires a published timestamp.");

        if (!isPublished && publishedOnUtc is not null)
            throw new DomainException("An unpublished subscription plan cannot have a published timestamp.");

        return new SubscriptionPlanVersion(
            Guid.CreateVersion7(),
            plan,
            isPublished,
            isAvailableForNewSales,
            createdOnUtc ?? DateTime.UtcNow,
            publishedOnUtc);
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
