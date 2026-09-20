using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionValueObjectIndependentTests
{
    [Fact]
    public void Published_packages_match_the_locked_keys_versions_prices_currency_and_cycles()
    {
        AssertPackage(SubscriptionPlan.Free, "free", 0m, SubscriptionCycle.Monthly);
        AssertPackage(SubscriptionPlan.Premium, "premium", 299m, SubscriptionCycle.Monthly);
        AssertPackage(SubscriptionPlan.PremiumPlus, "premium-plus", 2499m, SubscriptionCycle.Annual);
    }

    [Fact]
    public void Published_packages_encode_every_benefit_including_unavailable_states()
    {
        AssertBenefits(SubscriptionPlan.Free, new Dictionary<SubscriptionBenefitKey, bool>
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

        AssertBenefits(SubscriptionPlan.Premium, new Dictionary<SubscriptionBenefitKey, bool>
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

        AssertBenefits(SubscriptionPlan.PremiumPlus, AllBenefits().ToDictionary(x => x.Key, x => true));
    }

    [Fact]
    public void Published_packages_use_finite_limits_or_explicit_unlimited_limits_and_no_rollover()
    {
        AssertFinite(SubscriptionPlan.Free, 3, 5);
        Assert.Equal(SubscriptionRollover.None, SubscriptionPlan.Free.Rollover);

        AssertFinite(SubscriptionPlan.Premium, 10, 20);
        Assert.Equal(SubscriptionRollover.None, SubscriptionPlan.Premium.Rollover);

        AssertUnlimited(SubscriptionPlan.PremiumPlus.MemberLimit);
        AssertUnlimited(SubscriptionPlan.PremiumPlus.MonthlyBookingLimit);
        Assert.Equal(SubscriptionRollover.NotApplicable, SubscriptionPlan.PremiumPlus.Rollover);
    }

    [Fact]
    public void Invalid_plan_key_version_price_currency_and_cycle_are_rejected()
    {
        Assert.Throws<DomainException>(() => CreateCustom(key: " "));
        Assert.Throws<DomainException>(() => CreateCustom(version: 0));
        Assert.Throws<DomainException>(() => CreateCustom(version: -1));
        Assert.Throws<DomainException>(() => CreateCustom(price: -0.01m));
        Assert.Throws<DomainException>(() => CreateCustom(currency: "USD"));
        Assert.Throws<DomainException>(() => CreateCustom(currency: ""));
        Assert.Throws<DomainException>(() => CreateCustom(cycle: (SubscriptionCycle)99));
        Assert.Throws<DomainException>(() => SubscriptionCycles.Parse(99));
    }

    [Fact]
    public void Invalid_limits_and_benefit_keys_are_rejected()
    {
        Assert.Throws<DomainException>(() => SubscriptionLimit.Finite(0));
        Assert.Throws<DomainException>(() => SubscriptionLimit.Finite(-1));
        Assert.Throws<DomainException>(() => SubscriptionBenefit.Create((SubscriptionBenefitKey)99, true));
    }

    [Fact]
    public void Duplicate_or_missing_benefit_definitions_are_rejected()
    {
        var duplicate = AllBenefits().ToList();
        duplicate[1] = duplicate[0];
        Assert.Throws<DomainException>(() => CreateCustom(benefits: duplicate));

        Assert.Throws<DomainException>(() => CreateCustom(benefits: AllBenefits().Skip(1)));
    }

    [Fact]
    public void Null_benefit_entries_are_rejected_with_domain_exception()
    {
        var benefits = AllBenefits().Cast<SubscriptionBenefit?>().ToList();
        benefits[3] = null;

        Assert.Throws<DomainException>(() => CreateCustom(benefits: benefits!));
    }

    [Fact]
    public void Extra_or_invalid_benefit_key_sets_are_rejected()
    {
        var extra = AllBenefits().ToList();
        extra.Add(SubscriptionBenefit.Create(SubscriptionBenefitKey.Chatting, true));
        Assert.Throws<DomainException>(() => CreateCustom(benefits: extra));

        Assert.Throws<DomainException>(() =>
            CreateCustom(benefits: AllBenefits().Where(benefit => benefit.Key != SubscriptionBenefitKey.Library)));

        Assert.Throws<DomainException>(() => SubscriptionBenefit.Create((SubscriptionBenefitKey)99, true));
    }

    [Fact]
    public void Equality_includes_all_plan_components_and_value_objects()
    {
        SubscriptionPlan baseline = CreateCustom();

        Assert.Equal(baseline, CreateCustom());
        Assert.NotEqual(baseline, CreateCustom(key: "other"));
        Assert.NotEqual(baseline, CreateCustom(version: 2));
        Assert.NotEqual(baseline, CreateCustom(price: 11m));
        Assert.NotEqual(baseline, CreateCustom(cycle: SubscriptionCycle.Annual));
        Assert.NotEqual(baseline, CreateCustom(currency: "EGP", benefits: AllBenefits(false)));
        Assert.NotEqual(baseline, CreateCustom(memberLimit: SubscriptionLimit.Finite(4)));
        Assert.NotEqual(baseline, CreateCustom(bookingLimit: SubscriptionLimit.Finite(6)));
        Assert.NotEqual(baseline, CreateCustom(rollover: SubscriptionRollover.NotApplicable));
    }

    [Fact]
    public void Source_benefit_collection_is_snapshotted_and_exposed_as_read_only()
    {
        var source = AllBenefits().ToList();
        SubscriptionPlan plan = CreateCustom(benefits: source);

        source.Clear();
        source.Add(SubscriptionBenefit.Create(SubscriptionBenefitKey.Chatting, false));

        Assert.Equal(8, plan.Benefits.Count);
        Assert.True(plan.GetBenefit(SubscriptionBenefitKey.Chatting).IsIncluded);
        Assert.IsAssignableFrom<IReadOnlyList<SubscriptionBenefit>>(plan.Benefits);
        Assert.True(((IList<SubscriptionBenefit>)plan.Benefits).IsReadOnly);
    }

    private static SubscriptionPlan CreateCustom(
        string key = "custom",
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
            key, version, price, cycle, currency, benefits ?? AllBenefits(),
            memberLimit ?? SubscriptionLimit.Finite(3),
            bookingLimit ?? SubscriptionLimit.Finite(5),
            rollover);
    }

    private static IEnumerable<SubscriptionBenefit> AllBenefits(bool included = true)
    {
        return Enum.GetValues<SubscriptionBenefitKey>()
            .Select(key => SubscriptionBenefit.Create(key, included));
    }

    private static void AssertPackage(
        SubscriptionPlan plan,
        string key,
        decimal price,
        SubscriptionCycle cycle)
    {
        Assert.Equal(key, plan.Key);
        Assert.Equal(1, plan.Version);
        Assert.Equal("EGP", plan.Currency);
        Assert.Equal(price, plan.Price);
        Assert.Equal(cycle, plan.Cycle);
    }

    private static void AssertBenefits(
        SubscriptionPlan plan,
        IReadOnlyDictionary<SubscriptionBenefitKey, bool> expected)
    {
        Assert.Equal(Enum.GetValues<SubscriptionBenefitKey>().Length, plan.Benefits.Count);
        Assert.Equal(expected.Count, plan.Benefits.Select(x => x.Key).Distinct().Count());

        foreach ((SubscriptionBenefitKey key, bool included) in expected)
            Assert.Equal(included, plan.GetBenefit(key).IsIncluded);
    }

    private static void AssertFinite(SubscriptionPlan plan, int memberLimit, int bookingLimit)
    {
        Assert.Equal(SubscriptionLimitKind.Finite, plan.MemberLimit.Kind);
        Assert.Equal(memberLimit, plan.MemberLimit.Value);
        Assert.Equal(SubscriptionLimitKind.Finite, plan.MonthlyBookingLimit.Kind);
        Assert.Equal(bookingLimit, plan.MonthlyBookingLimit.Value);
    }

    private static void AssertUnlimited(SubscriptionLimit limit)
    {
        Assert.Equal(SubscriptionLimitKind.Unlimited, limit.Kind);
        Assert.True(limit.IsUnlimited);
        Assert.Null(limit.Value);
    }
}
