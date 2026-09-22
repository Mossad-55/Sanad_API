using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.Exceptions;
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
        Assert.Equal(subscription.CreatedOnUtc.AddYears(1), subscription.CurrentPeriodEndsOnUtc);
        Assert.True(subscription.AutoRenewEnabled);
        Assert.Null(subscription.CancellationRequestedOnUtc);
        Assert.NotEqual(Guid.Empty, subscription.LifecycleVersion);
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

    [Fact]
    public void Reenable_rejects_at_or_after_the_explicit_period_end()
    {
        DateTime created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime periodEnd = created.AddMonths(1);
        FamilySubscription subscription = FamilySubscription.Create(
            FamilyId.New(), SubscriptionPlanVersion.Create(SubscriptionPlan.Free), created, periodEnd);
        subscription.CancelRenewal(created.AddDays(1));

        Assert.Throws<DomainException>(() => subscription.ReenableAutoRenew(periodEnd));
        Assert.Throws<DomainException>(() => subscription.ReenableAutoRenew(periodEnd.AddTicks(1)));
    }

    [Fact]
    public void Lifecycle_version_changes_for_cancel_and_reenable()
    {
        var subscription = FamilySubscription.Create(
            FamilyId.New(),
            SubscriptionPlanVersion.Create(SubscriptionPlan.Free),
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(1));
        var createdVersion = subscription.LifecycleVersion;

        subscription.CancelRenewal(DateTime.UtcNow);
        var cancelledVersion = subscription.LifecycleVersion;
        subscription.ReenableAutoRenew(DateTime.UtcNow.AddMinutes(1));

        Assert.NotEqual(Guid.Empty, createdVersion);
        Assert.NotEqual(createdVersion, cancelledVersion);
        Assert.NotEqual(cancelledVersion, subscription.LifecycleVersion);
    }

    [Fact]
    public void Failed_renewal_opens_a_seven_day_grace_window_from_the_anchor()
    {
        DateTime created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime periodEnd = created.AddMonths(1);
        FamilySubscription subscription = FamilySubscription.Create(
            FamilyId.New(), SubscriptionPlanVersion.Create(SubscriptionPlan.Free), created, periodEnd);

        subscription.BeginRenewalGrace(periodEnd.AddMinutes(1));

        Assert.Equal(periodEnd.AddDays(7), subscription.RenewalGraceEndsOnUtc);
        Assert.Equal(periodEnd.AddMinutes(1), subscription.LastRenewalFailedOnUtc);
        Assert.True(subscription.IsWithinRenewalGrace(periodEnd.AddDays(6)));
        Assert.False(subscription.IsWithinRenewalGrace(periodEnd.AddDays(7)));
    }

    [Fact]
    public void Successful_renewal_advances_from_the_original_anchor_and_clears_grace()
    {
        DateTime created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime periodEnd = created.AddMonths(1);
        FamilySubscription subscription = FamilySubscription.Create(
            FamilyId.New(), SubscriptionPlanVersion.Create(SubscriptionPlan.Free), created, periodEnd);
        subscription.BeginRenewalGrace(periodEnd.AddMinutes(1));

        subscription.ApplySuccessfulRenewal(periodEnd.AddDays(3));

        Assert.Equal(periodEnd.AddMonths(1), subscription.CurrentPeriodEndsOnUtc);
        Assert.Null(subscription.RenewalGraceEndsOnUtc);
        Assert.Null(subscription.LastRenewalFailedOnUtc);
    }

    [Fact]
    public void Renewal_after_grace_expiry_is_rejected()
    {
        DateTime created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime periodEnd = created.AddMonths(1);
        FamilySubscription subscription = FamilySubscription.Create(
            FamilyId.New(), SubscriptionPlanVersion.Create(SubscriptionPlan.Free), created, periodEnd);
        subscription.BeginRenewalGrace(periodEnd);

        Assert.Throws<DomainException>(() =>
            subscription.ApplySuccessfulRenewal(periodEnd.AddDays(7)));
    }
}
