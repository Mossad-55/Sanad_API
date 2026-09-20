using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class FamilySubscriptionTests
{
    [Fact]
    public void Create_snapshots_plan_terms_and_starts_current()
    {
        SubscriptionPlanVersion version = SubscriptionPlanVersion.Create(SubscriptionPlan.PremiumPlus);
        FamilySubscription subscription = FamilySubscription.Create(FamilyId.New(), version);

        Assert.Equal(version.Key, subscription.PlanKey);
        Assert.Equal(version.Version, subscription.PlanVersion);
        Assert.Equal(version.Price, subscription.Price);
        Assert.Equal(version.Cycle, subscription.Cycle);
        Assert.Equal(version.MemberLimitValue, subscription.MemberLimitValue);
        Assert.Equal(version.MonthlyBookingLimitKind, subscription.MonthlyBookingLimitKind);
        Assert.Equal(version.Benefits, subscription.Benefits);
        Assert.True(subscription.IsCurrent);
    }

    [Fact]
    public void Current_pointer_can_be_turned_off_without_changing_the_snapshot()
    {
        FamilySubscription subscription = FamilySubscription.Create(
            FamilyId.New(),
            SubscriptionPlanVersion.Create(SubscriptionPlan.Free));
        decimal price = subscription.Price;

        subscription.MarkNotCurrent();

        Assert.False(subscription.IsCurrent);
        Assert.Equal(price, subscription.Price);
    }
}
