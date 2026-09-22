using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionQuoteTests
{
    [Fact]
    public async Task Owner_gets_server_calculated_tax_and_coupon_quote_without_writes()
    {
        await using var db = CreateContext();
        var owner = UserId.New();
        var now = DateTime.UtcNow;
        var family = Family.Create(owner);
        var plan = SubscriptionPlanVersion.Create(
            SubscriptionPlan.Premium, true, true, now.AddDays(-2), now.AddDays(-2));
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        db.SubscriptionCoupons.Add(SubscriptionCoupon.Create(
            "SAVE10", plan.Id, 10m, now.AddDays(1), now.AddDays(-1)));
        db.SubscriptionTaxRules.Add(SubscriptionTaxRule.Create(15m, 1, now.AddDays(-1), now.AddDays(-1)));
        await db.SaveChangesAsync();
        int beforeSubscriptions = await db.FamilySubscriptions.CountAsync();

        var result = await new CreateSubscriptionQuoteQueryHandler(db).Handle(
            new CreateSubscriptionQuoteCommand(owner, plan.Id, " save10 ", now), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(299m, result.Value.BasePrice);
        Assert.Equal(29.90m, result.Value.DiscountAmount);
        Assert.Equal(269.10m, result.Value.TaxableAmount);
        Assert.Equal(15m, result.Value.TaxRatePercentage);
        Assert.Equal(40.36m, result.Value.TaxAmount);
        Assert.Equal(309.46m, result.Value.TotalPayable);
        Assert.Equal("SAVE10", result.Value.CouponCode);
        Assert.False(result.Value.RecurringRenewalSupported);
        Assert.Equal(beforeSubscriptions, await db.FamilySubscriptions.CountAsync());
    }

    [Fact]
    public async Task Non_owner_cannot_request_quote()
    {
        await using var db = CreateContext();
        var owner = UserId.New();
        var otherUser = UserId.New();
        var now = DateTime.UtcNow;
        var family = Family.Create(owner);
        var plan = SubscriptionPlanVersion.Create(
            SubscriptionPlan.Premium, true, true, now.AddDays(-1), now.AddDays(-1));
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        db.SubscriptionTaxRules.Add(SubscriptionTaxRule.Create(0m, 1, now, now));
        await db.SaveChangesAsync();

        var result = await new CreateSubscriptionQuoteQueryHandler(db).Handle(
            new CreateSubscriptionQuoteCommand(otherUser, plan.Id, null, now), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Subscriptions.NotOwner", result.Error.Code);
    }

    [Fact]
    public async Task Draft_or_retired_plan_and_invalid_coupon_are_rejected()
    {
        await using var db = CreateContext();
        var owner = UserId.New();
        var now = DateTime.UtcNow;
        db.Families.Add(Family.Create(owner));
        var unavailable = SubscriptionPlanVersion.Create(
            SubscriptionPlan.Premium, true, false, now, now);
        db.SubscriptionPlanVersions.Add(unavailable);
        db.SubscriptionTaxRules.Add(SubscriptionTaxRule.Create(0m, 1, now, now));
        await db.SaveChangesAsync();

        var unavailableResult = await new CreateSubscriptionQuoteQueryHandler(db).Handle(
            new CreateSubscriptionQuoteCommand(owner, unavailable.Id, null, now), default);
        var missingCouponResult = await new CreateSubscriptionQuoteQueryHandler(db).Handle(
            new CreateSubscriptionQuoteCommand(owner, unavailable.Id, "MISSING", now), default);

        Assert.False(unavailableResult.IsSuccess);
        Assert.Equal("Subscriptions.Quote.PlanNotFound", unavailableResult.Error.Code);
        Assert.False(missingCouponResult.IsSuccess);
        Assert.Equal("Subscriptions.Quote.PlanNotFound", missingCouponResult.Error.Code);
    }

    [Fact]
    public async Task Expired_or_wrong_plan_coupon_is_rejected()
    {
        await using var db = CreateContext();
        var owner = UserId.New();
        var now = DateTime.UtcNow;
        db.Families.Add(Family.Create(owner));
        var plan = SubscriptionPlanVersion.Create(
            SubscriptionPlan.Premium, true, true, now.AddDays(-2), now.AddDays(-2));
        var otherPlan = SubscriptionPlanVersion.Create(
            SubscriptionPlan.PremiumPlus, true, true, now.AddDays(-2), now.AddDays(-2));
        db.SubscriptionPlanVersions.AddRange(plan, otherPlan);
        db.SubscriptionCoupons.Add(SubscriptionCoupon.Create(
            "EXPIRED", otherPlan.Id, 10m, now.AddMinutes(-1), now.AddDays(-2)));
        db.SubscriptionTaxRules.Add(SubscriptionTaxRule.Create(0m, 1, now.AddDays(-1), now.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await new CreateSubscriptionQuoteQueryHandler(db).Handle(
            new CreateSubscriptionQuoteCommand(owner, plan.Id, "EXPIRED", now), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Subscriptions.Quote.CouponInvalid", result.Error.Code);
    }

    [Fact]
    public async Task Future_effective_tax_rule_is_not_used_before_its_effective_time()
    {
        await using var db = CreateContext();
        var owner = UserId.New();
        var now = DateTime.UtcNow;
        var plan = SubscriptionPlanVersion.Create(
            SubscriptionPlan.Premium, true, true, now.AddDays(-1), now.AddDays(-1));
        db.Families.Add(Family.Create(owner));
        db.SubscriptionPlanVersions.Add(plan);
        db.SubscriptionTaxRules.Add(SubscriptionTaxRule.Create(
            15m, 1, now.AddHours(1), now.AddHours(-1)));
        await db.SaveChangesAsync();

        var result = await new CreateSubscriptionQuoteQueryHandler(db).Handle(
            new CreateSubscriptionQuoteCommand(owner, plan.Id, null, now), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Subscriptions.Quote.TaxNotConfigured", result.Error.Code);
    }

    private static FamiliesDbContext CreateContext() => new(
        new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
