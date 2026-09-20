using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Subscriptions;

public sealed class FamilySubscription : Entity<Guid>
{
    private readonly List<SubscriptionBenefit> _benefits = [];

    private FamilySubscription()
    {
    }

    private FamilySubscription(
        Guid id,
        FamilyId familyId,
        SubscriptionPlanVersion plan,
        DateTime createdOnUtc)
        : base(id)
    {
        FamilyId = familyId;
        PlanKey = plan.Key;
        PlanVersion = plan.Version;
        Price = plan.Price;
        Cycle = plan.Cycle;
        Currency = plan.Currency;
        MemberLimitKind = plan.MemberLimitKind;
        MemberLimitValue = plan.MemberLimitValue;
        MonthlyBookingLimitKind = plan.MonthlyBookingLimitKind;
        MonthlyBookingLimitValue = plan.MonthlyBookingLimitValue;
        Rollover = plan.Rollover;
        _benefits.AddRange(plan.Benefits);
        IsCurrent = true;
        CreatedOnUtc = createdOnUtc;
    }

    public FamilyId FamilyId { get; private set; }
    public string PlanKey { get; private set; } = string.Empty;
    public int PlanVersion { get; private set; }
    public decimal Price { get; private set; }
    public SubscriptionCycle Cycle { get; private set; }
    public string Currency { get; private set; } = SubscriptionPlan.DefaultCurrency;
    public SubscriptionLimitKind MemberLimitKind { get; private set; }
    public int? MemberLimitValue { get; private set; }
    public SubscriptionLimitKind MonthlyBookingLimitKind { get; private set; }
    public int? MonthlyBookingLimitValue { get; private set; }
    public SubscriptionRollover Rollover { get; private set; }
    public bool IsCurrent { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public IReadOnlyCollection<SubscriptionBenefit> Benefits => _benefits.AsReadOnly();

    public static FamilySubscription Create(
        FamilyId familyId,
        SubscriptionPlanVersion plan,
        DateTime? createdOnUtc = null)
    {
        if (familyId == FamilyId.Empty)
            throw new DomainException("A family subscription requires a family.");

        ArgumentNullException.ThrowIfNull(plan);

        return new FamilySubscription(Guid.CreateVersion7(), familyId, plan, createdOnUtc ?? DateTime.UtcNow);
    }

    public void MarkNotCurrent()
    {
        IsCurrent = false;
    }
}
