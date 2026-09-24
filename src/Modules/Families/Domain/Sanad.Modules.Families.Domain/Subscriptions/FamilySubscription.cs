using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Subscriptions;

public sealed class FamilySubscription : Entity<Guid>
{
    public const int RenewalGracePeriodDays = 7;

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
    public DateTime? RenewalGraceEndsOnUtc { get; private set; }
    public DateTime? LastRenewalFailedOnUtc { get; private set; }
    public string? PaymobSubscriptionId { get; private set; }
    public string? PaymobSubscriptionState { get; private set; }
    public DateTime? PaymobNextBillingOnUtc { get; private set; }
    public string? PaymobLastCallbackKey { get; private set; }
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

    public bool IsWithinRenewalGrace(DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Renewal evaluation time must be UTC.");

        return IsCurrent &&
               RenewalGraceEndsOnUtc is not null &&
               nowUtc < RenewalGraceEndsOnUtc.Value;
    }

    public void BeginRenewalGrace(DateTime failedOnUtc)
    {
        if (!IsCurrent)
            throw new DomainException("Only the current subscription can enter renewal grace.");
        if (failedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Renewal failure time must be UTC.");

        if (RenewalGraceEndsOnUtc is null)
        {
            RenewalGraceEndsOnUtc = CurrentPeriodEndsOnUtc.AddDays(RenewalGracePeriodDays);
            LastRenewalFailedOnUtc = failedOnUtc;
        }
        LifecycleVersion = Guid.NewGuid();
    }

    public void AssociatePaymobSubscription(
        string providerSubscriptionId,
        string? state,
        DateTime? nextBillingOnUtc,
        string? callbackKey = null)
    {
        if (string.IsNullOrWhiteSpace(providerSubscriptionId))
            throw new DomainException("A Paymob subscription requires a provider subscription id.");
        if (nextBillingOnUtc is not null && nextBillingOnUtc.Value.Kind != DateTimeKind.Utc)
            throw new DomainException("Paymob subscription billing time must be UTC.");

        PaymobSubscriptionId = providerSubscriptionId.Trim();
        if (!string.IsNullOrWhiteSpace(state))
            PaymobSubscriptionState = state.Trim();
        if (nextBillingOnUtc is not null)
            PaymobNextBillingOnUtc = nextBillingOnUtc;
        if (!string.IsNullOrWhiteSpace(callbackKey))
            PaymobLastCallbackKey = callbackKey;
        LifecycleVersion = Guid.NewGuid();
    }

    public bool HasProcessedPaymobCallback(string callbackKey) =>
        !string.IsNullOrWhiteSpace(callbackKey) &&
        string.Equals(PaymobLastCallbackKey, callbackKey, StringComparison.Ordinal);

    public void ApplySuccessfulRenewal(DateTime settledOnUtc)
    {
        if (!IsCurrent)
            throw new DomainException("Only the current subscription can be renewed.");
        if (settledOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Renewal settlement time must be UTC.");
        if (settledOnUtc < CurrentPeriodEndsOnUtc)
            throw new DomainException("A subscription cannot renew before its current period ends.");
        if (RenewalGraceEndsOnUtc is not null && settledOnUtc >= RenewalGraceEndsOnUtc.Value)
            throw new DomainException("The subscription renewal grace period has ended.");

        CurrentPeriodEndsOnUtc = CalculatePeriodEnd(CurrentPeriodEndsOnUtc, Cycle);
        RenewalGraceEndsOnUtc = null;
        LastRenewalFailedOnUtc = null;
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
