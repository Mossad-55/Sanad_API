using System.Collections.ObjectModel;
using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Subscriptions;

public enum SubscriptionRollover
{
    None = 1,
    NotApplicable = 2
}

public sealed class SubscriptionPlan : ValueObject
{
    public const string DefaultCurrency = "EGP";
    public const string FreeKey = "free";
    public const string PremiumKey = "premium";
    public const string PremiumPlusKey = "premium-plus";

    private SubscriptionPlan(
        string key,
        int version,
        decimal price,
        SubscriptionCycle cycle,
        string currency,
        IEnumerable<SubscriptionBenefit> benefits,
        SubscriptionLimit memberLimit,
        SubscriptionLimit monthlyBookingLimit,
        SubscriptionRollover rollover)
    {
        Key = key;
        Version = version;
        Price = price;
        Cycle = cycle;
        Currency = currency;
        Benefits = new ReadOnlyCollection<SubscriptionBenefit>(benefits.ToList());
        MemberLimit = memberLimit;
        MonthlyBookingLimit = monthlyBookingLimit;
        Rollover = rollover;
    }

    public string Key { get; }
    public int Version { get; }
    public decimal Price { get; }
    public SubscriptionCycle Cycle { get; }
    public string Currency { get; }
    public IReadOnlyList<SubscriptionBenefit> Benefits { get; }
    public SubscriptionLimit MemberLimit { get; }
    public SubscriptionLimit MonthlyBookingLimit { get; }
    public SubscriptionRollover Rollover { get; }

    public SubscriptionBenefit GetBenefit(SubscriptionBenefitKey key)
    {
        return Benefits.Single(benefit => benefit.Key == key);
    }

    public static SubscriptionPlan Create(
        string key,
        int version,
        decimal price,
        SubscriptionCycle cycle,
        string currency,
        IEnumerable<SubscriptionBenefit> benefits,
        SubscriptionLimit memberLimit,
        SubscriptionLimit monthlyBookingLimit,
        SubscriptionRollover rollover)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new DomainException("Subscription plan key is required.");

        if (version <= 0)
            throw new DomainException("Subscription plan version must be positive.");

        if (price < 0)
            throw new DomainException("Subscription plan price cannot be negative.");

        if (!Enum.IsDefined(cycle))
            throw new DomainException("Subscription billing cycle is invalid.");

        if (!string.Equals(currency, DefaultCurrency, StringComparison.Ordinal))
            throw new DomainException("Subscription currency is invalid.");

        ArgumentNullException.ThrowIfNull(benefits);
        ArgumentNullException.ThrowIfNull(memberLimit);
        ArgumentNullException.ThrowIfNull(monthlyBookingLimit);

        var benefitList = benefits.ToList();
        if (benefitList.Count != Enum.GetValues<SubscriptionBenefitKey>().Length
            || benefitList.Select(benefit => benefit.Key).Distinct().Count() != benefitList.Count)
            throw new DomainException("A subscription plan must define every benefit exactly once.");

        if (!Enum.IsDefined(rollover))
            throw new DomainException("Subscription rollover policy is invalid.");

        return new SubscriptionPlan(
            key.Trim(),
            version,
            decimal.Round(price, 2, MidpointRounding.ToEven),
            cycle,
            currency,
            benefitList,
            memberLimit,
            monthlyBookingLimit,
            rollover);
    }

    public static SubscriptionPlan Free => CreatePackage(
        FreeKey, 1, 0m, SubscriptionCycle.Monthly,
        SubscriptionLimit.Finite(3), SubscriptionLimit.Finite(5), SubscriptionRollover.None,
        SubscriptionBenefitKey.FamilyActivityTimeline,
        SubscriptionBenefitKey.AdvancedSearchFilters,
        SubscriptionBenefitKey.MedicalSummaryExportAndSecureSharing,
        SubscriptionBenefitKey.PremiumContent);

    public static SubscriptionPlan Premium => CreatePackage(
        PremiumKey, 1, 299m, SubscriptionCycle.Monthly,
        SubscriptionLimit.Finite(10), SubscriptionLimit.Finite(20), SubscriptionRollover.None,
        SubscriptionBenefitKey.PremiumContent);

    public static SubscriptionPlan PremiumPlus => CreatePackage(
        PremiumPlusKey, 1, 2499m, SubscriptionCycle.Annual,
        SubscriptionLimit.Unlimited, SubscriptionLimit.Unlimited, SubscriptionRollover.NotApplicable);

    private static SubscriptionPlan CreatePackage(
        string key,
        int version,
        decimal price,
        SubscriptionCycle cycle,
        SubscriptionLimit memberLimit,
        SubscriptionLimit monthlyBookingLimit,
        SubscriptionRollover rollover,
        params SubscriptionBenefitKey[] unavailable)
    {
        var unavailableSet = unavailable.ToHashSet();
        var benefits = Enum.GetValues<SubscriptionBenefitKey>()
            .Select(benefit => SubscriptionBenefit.Create(benefit, !unavailableSet.Contains(benefit)));

        return Create(key, version, price, cycle, DefaultCurrency, benefits,
            memberLimit, monthlyBookingLimit, rollover);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Key;
        yield return Version;
        yield return Price;
        yield return Cycle;
        yield return Currency;
        foreach (var benefit in Benefits)
            yield return benefit;
        yield return MemberLimit;
        yield return MonthlyBookingLimit;
        yield return Rollover;
    }
}
