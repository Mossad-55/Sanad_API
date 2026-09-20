using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionValueObjectTests
{
    [Fact]
    public void Locked_plans_have_exact_metadata_and_limits()
    {
        SubscriptionPlan free = SubscriptionPlan.Free;
        Assert.Equal("free", free.Key);
        Assert.Equal(1, free.Version);
        Assert.Equal("EGP", free.Currency);
        Assert.Equal(SubscriptionCycle.Monthly, free.Cycle);
        Assert.Equal(0m, free.Price);
        Assert.Equal(SubscriptionLimitKind.Finite, free.MemberLimit.Kind);
        Assert.Equal(3, free.MemberLimit.Value);
        Assert.Equal(SubscriptionLimitKind.Finite, free.MonthlyBookingLimit.Kind);
        Assert.Equal(5, free.MonthlyBookingLimit.Value);
        Assert.Equal(SubscriptionRollover.None, free.Rollover);

        SubscriptionPlan premium = SubscriptionPlan.Premium;
        Assert.Equal("premium", premium.Key);
        Assert.Equal(1, premium.Version);
        Assert.Equal("EGP", premium.Currency);
        Assert.Equal(SubscriptionCycle.Monthly, premium.Cycle);
        Assert.Equal(299m, premium.Price);
        Assert.Equal(SubscriptionLimitKind.Finite, premium.MemberLimit.Kind);
        Assert.Equal(10, premium.MemberLimit.Value);
        Assert.Equal(SubscriptionLimitKind.Finite, premium.MonthlyBookingLimit.Kind);
        Assert.Equal(20, premium.MonthlyBookingLimit.Value);
        Assert.Equal(SubscriptionRollover.None, premium.Rollover);

        SubscriptionPlan premiumPlus = SubscriptionPlan.PremiumPlus;
        Assert.Equal("premium-plus", premiumPlus.Key);
        Assert.Equal(1, premiumPlus.Version);
        Assert.Equal("EGP", premiumPlus.Currency);
        Assert.Equal(SubscriptionCycle.Annual, premiumPlus.Cycle);
        Assert.Equal(2499m, premiumPlus.Price);
        Assert.Equal(SubscriptionLimitKind.Unlimited, premiumPlus.MemberLimit.Kind);
        Assert.Null(premiumPlus.MemberLimit.Value);
        Assert.Equal(SubscriptionLimitKind.Unlimited, premiumPlus.MonthlyBookingLimit.Kind);
        Assert.Null(premiumPlus.MonthlyBookingLimit.Value);
        Assert.Equal(SubscriptionRollover.NotApplicable, premiumPlus.Rollover);
    }

    [Fact]
    public void Locked_plans_have_every_benefit_state_explicitly_encoded()
    {
        AssertBenefitStates(SubscriptionPlan.Free, new Dictionary<SubscriptionBenefitKey, bool>
        {
            [SubscriptionBenefitKey.Chatting] = true,
            [SubscriptionBenefitKey.Library] = true,
            [SubscriptionBenefitKey.CommunityForum] = true,
            [SubscriptionBenefitKey.FamilyActivityTimeline] = false,
            [SubscriptionBenefitKey.BasicSearch] = true,
            [SubscriptionBenefitKey.AdvancedSearchFilters] = false,
            [SubscriptionBenefitKey.MedicalSummaryExportAndSecureSharing] = false,
            [SubscriptionBenefitKey.PremiumContent] = false
        });

        AssertBenefitStates(SubscriptionPlan.Premium, new Dictionary<SubscriptionBenefitKey, bool>
        {
            [SubscriptionBenefitKey.Chatting] = true,
            [SubscriptionBenefitKey.Library] = true,
            [SubscriptionBenefitKey.CommunityForum] = true,
            [SubscriptionBenefitKey.FamilyActivityTimeline] = true,
            [SubscriptionBenefitKey.BasicSearch] = true,
            [SubscriptionBenefitKey.AdvancedSearchFilters] = true,
            [SubscriptionBenefitKey.MedicalSummaryExportAndSecureSharing] = true,
            [SubscriptionBenefitKey.PremiumContent] = false
        });

        AssertBenefitStates(SubscriptionPlan.PremiumPlus, new Dictionary<SubscriptionBenefitKey, bool>
        {
            [SubscriptionBenefitKey.Chatting] = true,
            [SubscriptionBenefitKey.Library] = true,
            [SubscriptionBenefitKey.CommunityForum] = true,
            [SubscriptionBenefitKey.FamilyActivityTimeline] = true,
            [SubscriptionBenefitKey.BasicSearch] = true,
            [SubscriptionBenefitKey.AdvancedSearchFilters] = true,
            [SubscriptionBenefitKey.MedicalSummaryExportAndSecureSharing] = true,
            [SubscriptionBenefitKey.PremiumContent] = true
        });
    }

    [Fact]
    public void Equality_covers_cycle_currency_benefits_limits_and_rollover()
    {
        SubscriptionPlan baseline = CreateCustom();

        Assert.Equal(baseline, CreateCustom());
        Assert.NotEqual(baseline, CreateCustom(cycle: SubscriptionCycle.Annual));
        Assert.NotEqual(baseline, CreateCustom(benefits: AllBenefits(false)));
        Assert.NotEqual(baseline, CreateCustom(memberLimit: SubscriptionLimit.Finite(4)));
        Assert.NotEqual(baseline, CreateCustom(bookingLimit: SubscriptionLimit.Finite(6)));
        Assert.NotEqual(baseline, CreateCustom(rollover: SubscriptionRollover.NotApplicable));
        Assert.Equal("EGP", CreateCustom(currency: "EGP").Currency);
    }

    [Fact]
    public void Duplicate_or_missing_benefit_definitions_fail()
    {
        var duplicate = AllBenefits().ToList();
        duplicate[1] = duplicate[0];
        Assert.Throws<DomainException>(() => CreateCustom(benefits: duplicate));

        var missing = AllBenefits().Skip(1);
        Assert.Throws<DomainException>(() => CreateCustom(benefits: missing));
    }

    [Fact]
    public void Invalid_rollover_currency_and_cycle_fail()
    {
        Assert.Throws<DomainException>(() => CreateCustom(rollover: (SubscriptionRollover)99));
        Assert.Throws<DomainException>(() => CreateCustom(currency: "USD"));
        Assert.Throws<DomainException>(() => CreateCustom(cycle: (SubscriptionCycle)99));
    }

    [Fact]
    public void Invalid_plan_version_price_limit_and_benefit_fail()
    {
        Assert.Throws<DomainException>(() => CreateCustom(version: 0));
        Assert.Throws<DomainException>(() => CreateCustom(price: -1m));
        Assert.Throws<DomainException>(() => SubscriptionLimit.Finite(0));
        Assert.Throws<DomainException>(() => SubscriptionLimit.Finite(-1));
        Assert.Throws<DomainException>(() => SubscriptionBenefit.Create((SubscriptionBenefitKey)99, true));
    }

    [Fact]
    public void Source_benefit_collection_is_snapshotted()
    {
        var source = AllBenefits().ToList();
        SubscriptionPlan plan = CreateCustom(benefits: source);

        source.Clear();
        source.Add(SubscriptionBenefit.Create(SubscriptionBenefitKey.Chatting, false));

        Assert.Equal(8, plan.Benefits.Count);
        Assert.True(plan.GetBenefit(SubscriptionBenefitKey.Chatting).IsIncluded);
    }

    private static SubscriptionPlan CreateCustom(
        int version = 1,
        decimal price = 10m,
        SubscriptionCycle cycle = SubscriptionCycle.Monthly,
        string currency = "EGP",
        IEnumerable<SubscriptionBenefit>? benefits = null,
        SubscriptionLimit? memberLimit = null,
        SubscriptionLimit? bookingLimit = null,
        SubscriptionRollover rollover = SubscriptionRollover.None)
    {
        return SubscriptionPlan.Create(
            "custom", version, price, cycle, currency, benefits ?? AllBenefits(),
            memberLimit ?? SubscriptionLimit.Finite(3),
            bookingLimit ?? SubscriptionLimit.Finite(5),
            rollover);
    }

    private static IEnumerable<SubscriptionBenefit> AllBenefits(bool included = true)
    {
        return Enum.GetValues<SubscriptionBenefitKey>()
            .Select(key => SubscriptionBenefit.Create(key, included));
    }

    private static void AssertBenefitStates(
        SubscriptionPlan plan,
        IReadOnlyDictionary<SubscriptionBenefitKey, bool> expected)
    {
        Assert.Equal(expected.Count, plan.Benefits.Count);
        foreach ((SubscriptionBenefitKey key, bool isIncluded) in expected)
            Assert.Equal(isIncluded, plan.GetBenefit(key).IsIncluded);
    }
}
