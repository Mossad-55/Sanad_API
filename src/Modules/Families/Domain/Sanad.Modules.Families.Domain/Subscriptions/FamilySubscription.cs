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
        AutoRenewEnabled = true;
        CurrentPeriodEndsOnUtc = CalculatePeriodEnd(createdOnUtc, plan.Cycle);
        LifecycleVersion = Guid.NewGuid();
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
    public bool AutoRenewEnabled { get; private set; }
    public DateTime? CancellationRequestedOnUtc { get; private set; }
    public DateTime CurrentPeriodEndsOnUtc { get; private set; }
    public Guid LifecycleVersion { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public IReadOnlyCollection<SubscriptionBenefit> Benefits => _benefits.AsReadOnly();

    public static FamilySubscription Create(
        FamilyId familyId,
        SubscriptionPlanVersion plan,
        DateTime? createdOnUtc = null,
        DateTime? currentPeriodEndsOnUtc = null)
    {
        if (familyId == FamilyId.Empty)
            throw new DomainException("A family subscription requires a family.");

        ArgumentNullException.ThrowIfNull(plan);

        var created = createdOnUtc ?? DateTime.UtcNow;
        var subscription = new FamilySubscription(Guid.CreateVersion7(), familyId, plan, created);
        if (currentPeriodEndsOnUtc is not null)
            subscription.CurrentPeriodEndsOnUtc = currentPeriodEndsOnUtc.Value;
        return subscription;
    }

    public void MarkNotCurrent()
    {
        IsCurrent = false;
    }

    public void CancelRenewal(DateTime? requestedOnUtc = null)
    {
        if (!IsCurrent)
            throw new DomainException("Only the current subscription can cancel renewal.");
        if (!AutoRenewEnabled || CancellationRequestedOnUtc is not null)
            throw new DomainException("Subscription renewal cancellation was already requested.");

        AutoRenewEnabled = false;
        CancellationRequestedOnUtc = requestedOnUtc ?? DateTime.UtcNow;
        LifecycleVersion = Guid.NewGuid();
    }

    public void ReenableAutoRenew(DateTime nowUtc)
    {
        if (!IsCurrent)
            throw new DomainException("Only the current subscription can re-enable auto-renew.");
        if (CancellationRequestedOnUtc is null)
            throw new DomainException("Subscription renewal cancellation was not requested.");
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Subscription re-enable time must be UTC.");
        if (nowUtc >= CurrentPeriodEndsOnUtc)
            throw new DomainException("Subscription current period has ended.");

        AutoRenewEnabled = true;
        CancellationRequestedOnUtc = null;
        LifecycleVersion = Guid.NewGuid();
    }

    private static DateTime CalculatePeriodEnd(DateTime createdOnUtc, SubscriptionCycle cycle) =>
        cycle switch
        {
            SubscriptionCycle.Monthly => createdOnUtc.AddMonths(1),
            SubscriptionCycle.Annual => createdOnUtc.AddYears(1),
            _ => throw new DomainException("Subscription billing cycle is invalid.")
        };
}
