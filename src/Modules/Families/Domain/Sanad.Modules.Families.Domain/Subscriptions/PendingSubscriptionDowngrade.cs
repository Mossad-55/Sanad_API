using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Subscriptions;

public sealed class PendingSubscriptionDowngrade
{
    private readonly List<SubscriptionBenefit> _benefits = [];
    private PendingSubscriptionDowngrade() { }

    private PendingSubscriptionDowngrade(SubscriptionPlanVersion plan)
    {
        PlanKey = plan.Key; PlanVersion = plan.Version; Price = plan.Price; Cycle = plan.Cycle;
        Currency = plan.Currency; MemberLimitKind = plan.MemberLimitKind; MemberLimitValue = plan.MemberLimitValue;
        MonthlyBookingLimitKind = plan.MonthlyBookingLimitKind; MonthlyBookingLimitValue = plan.MonthlyBookingLimitValue;
        Rollover = plan.Rollover;
        _benefits.AddRange(plan.Benefits.Select(item => SubscriptionBenefit.Create(item.Key, item.IsIncluded)));
    }
    public string PlanKey { get; private set; } = string.Empty;
    public int PlanVersion { get; private set; }
    public decimal Price { get; private set; }
    public SubscriptionCycle Cycle { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public SubscriptionLimitKind MemberLimitKind { get; private set; }
    public int? MemberLimitValue { get; private set; }
    public SubscriptionLimitKind MonthlyBookingLimitKind { get; private set; }
    public int? MonthlyBookingLimitValue { get; private set; }
    public SubscriptionRollover Rollover { get; private set; }
    public IReadOnlyCollection<SubscriptionBenefit> Benefits => _benefits.AsReadOnly();
    public static PendingSubscriptionDowngrade From(SubscriptionPlanVersion plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(plan.Currency)) throw new DomainException("Pending downgrade plan is invalid.");
        return new PendingSubscriptionDowngrade(plan);
    }
}
