using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionPlanVersionTests
{
    [Fact]
    public void Create_copies_the_complete_plan_catalog_terms()
    {
        SubscriptionPlanVersion version = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium, true, true, DateTime.UtcNow, DateTime.UtcNow);

        Assert.Equal("premium", version.Key);
        Assert.Equal(1, version.Version);
        Assert.Equal(299m, version.Price);
        Assert.Equal(SubscriptionCycle.Monthly, version.Cycle);
        Assert.Equal(SubscriptionLimitKind.Finite, version.MemberLimitKind);
        Assert.Equal(10, version.MemberLimitValue);
        Assert.Equal(SubscriptionLimitKind.Finite, version.MonthlyBookingLimitKind);
        Assert.Equal(20, version.MonthlyBookingLimitValue);
        Assert.Equal(8, version.Benefits.Count);
        Assert.True(version.IsPublished);
    }

    [Fact]
    public void Retirement_only_disables_new_sales()
    {
        SubscriptionPlanVersion version = SubscriptionPlanVersion.Create(SubscriptionPlan.Free);

        version.RetireFromNewSales();

        Assert.False(version.IsAvailableForNewSales);
        Assert.Equal(SubscriptionPlan.Free.Key, version.Key);
        Assert.Equal(SubscriptionPlan.Free.Version, version.Version);
    }
}
