using Sanad.Modules.Finance.Domain;

namespace Sanad.UnitTests.Finance;

/// <summary>
/// W2-F0 money core: the EGP piastre value object. Run green at the W2
/// integration gate by the Mastermind.
/// </summary>
public sealed class MoneyTests
{
    [Fact]
    public void Currency_is_fixed_to_EGP_and_stored_as_piastre_minor_units()
    {
        Money money = Money.FromMajorUnits(12.34m);

        Assert.Equal("EGP", money.Currency);
        Assert.Equal(Money.CurrencyCode, money.Currency);
        Assert.Equal(1234L, money.MinorUnits);
        Assert.Equal(12.34m, money.MajorUnits);
    }

    [Fact]
    public void Conversion_from_decimal_rounds_half_to_even()
    {
        // .5 piastre cases: 0.005 rounds to the even piastre (0.00), 0.015 to 0.02,
        // 0.025 to 0.02, 0.035 to 0.04 — ToEven, never AwayFromZero.
        Assert.Equal(0L, Money.FromMajorUnits(0.005m).MinorUnits);
        Assert.Equal(2L, Money.FromMajorUnits(0.015m).MinorUnits);
        Assert.Equal(2L, Money.FromMajorUnits(0.025m).MinorUnits);
        Assert.Equal(4L, Money.FromMajorUnits(0.035m).MinorUnits);
        Assert.Equal(100L, Money.FromMajorUnits(1.005m).MinorUnits);
        Assert.Equal(100L, Money.FromMajorUnits(1.0m).MinorUnits);
        Assert.Equal(101L, Money.FromMajorUnits(1.010m).MinorUnits);
    }

    [Fact]
    public void Conversion_from_decimal_rounds_half_to_even_symmetrically_for_negatives()
    {
        Assert.Equal(0L, Money.FromMajorUnits(-0.005m).MinorUnits);
        Assert.Equal(-2L, Money.FromMajorUnits(-0.015m).MinorUnits);
        Assert.Equal(-2L, Money.FromMajorUnits(-0.025m).MinorUnits);
        Assert.Equal(-4L, Money.FromMajorUnits(-0.035m).MinorUnits);
    }

    [Fact]
    public void Exact_half_piastre_never_rounds_away_from_zero()
    {
        // 1.005 EGP is exactly 100.5 piastres. AwayFromZero would give 101;
        // ToEven gives the even 100. This is the contract's rounding rule.
        Assert.Equal(100L, Money.FromMajorUnits(1.005m).MinorUnits);
        Assert.NotEqual(101L, Money.FromMajorUnits(1.005m).MinorUnits);
    }

    [Fact]
    public void Round_trip_through_major_units_is_lossless_at_piastre_precision()
    {
        foreach (long piastres in new long[] { 0, 1, 99, 100, 1234, -1, -1234, 9_999_999 })
        {
            Money original = Money.FromMinorUnits(piastres);
            Assert.Equal(piastres, Money.FromMajorUnits(original.MajorUnits).MinorUnits);
        }
    }

    [Fact]
    public void Addition_and_subtraction_are_exact_integer_piastre_arithmetic()
    {
        Money a = Money.FromMajorUnits(10.10m);
        Money b = Money.FromMajorUnits(0.20m);

        Assert.Equal(1030L, a.Add(b).MinorUnits);
        Assert.Equal(990L, a.Subtract(b).MinorUnits);
        // 0.1 + 0.2 is a classic binary floating-point trap; piastre integers
        // make it exactly 30.
        Assert.Equal(30L, Money.FromMajorUnits(0.10m).Add(Money.FromMajorUnits(0.20m)).MinorUnits);
    }

    [Fact]
    public void Operators_delegate_to_the_exact_operations()
    {
        Money a = Money.FromMajorUnits(5.00m);
        Money b = Money.FromMajorUnits(2.50m);

        Assert.Equal(750L, (a + b).MinorUnits);
        Assert.Equal(250L, (a - b).MinorUnits);
        Assert.Equal(-250L, (-b).MinorUnits);
        Assert.True(a > b);
        Assert.True(b < a);
        Money aEqual = Money.FromMajorUnits(5.00m);
        Assert.True(a >= aEqual);
        Assert.True(b <= a);
    }

    [Fact]
    public void ScaleBy_ratio_prorates_without_losing_precision()
    {
        // One day out of a 30-day month on a 300.00 EGP award is exactly 10.00.
        Money award = Money.FromMajorUnits(300.00m);

        Assert.Equal(1000L, award.ScaleBy(1, 30).MinorUnits);
        Assert.Equal(15000L, award.ScaleBy(15, 30).MinorUnits);
        Assert.Equal(30000L, award.ScaleBy(30, 30).MinorUnits);
    }

    [Fact]
    public void ScaleBy_rounds_half_to_even_on_the_piastre()
    {
        // 1 piastre * 1/8 = 0.125 piastres -> 0 (even); * 3/8 = 0.375 -> 0 (even)
        // is wrong for .375 (rounds to 0 only below .5), so assert the real values.
        Assert.Equal(0L, Money.FromMinorUnits(1).ScaleBy(1, 8).MinorUnits);
        Assert.Equal(1L, Money.FromMinorUnits(1).ScaleBy(5, 8).MinorUnits);
        Assert.Equal(2L, Money.FromMinorUnits(1).ScaleBy(19, 8).MinorUnits);

        // Exact half piastre: 25 * 1/50 = 0.5 -> 0 (even), 75 * 1/50 = 1.5 -> 2 (even).
        Assert.Equal(0L, Money.FromMinorUnits(25).ScaleBy(1, 50).MinorUnits);
        Assert.Equal(2L, Money.FromMinorUnits(75).ScaleBy(1, 50).MinorUnits);
    }

    [Fact]
    public void Proration_floor_is_zero_and_never_rounds_up()
    {
        // A one-second-remaining period can legitimately compute a 0-piastre
        // award (Owner decision #31). No minimum-award rule exists.
        Money large = Money.FromMajorUnits(1000.00m);

        Money almostNothing = large.ScaleBy(1, 31_536_000); // one second of a year
        Assert.Equal(0L, almostNothing.MinorUnits);
        Assert.Equal(Money.Zero, almostNothing);
        Assert.True(almostNothing.IsZero);
        Assert.False(almostNothing.IsNegative);

        Assert.Equal(0L, Money.FromMinorUnits(1).ScaleBy(1, 3).MinorUnits);
        Assert.Equal(0L, large.ScaleBy(0, 1).MinorUnits);
    }

    [Fact]
    public void ScaleBy_rejects_a_zero_denominator()
    {
        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(
            () => Money.FromMajorUnits(10m).ScaleBy(1, 0));
    }

    [Fact]
    public void ScaleBy_decimal_ratio_matches_the_integer_ratio()
    {
        Money award = Money.FromMajorUnits(300.00m);

        Assert.Equal(
            award.ScaleBy(1, 30).MinorUnits,
            award.ScaleBy(1m / 30m).MinorUnits);
    }

    [Fact]
    public void Zero_is_a_valid_amount_and_is_not_negative()
    {
        Assert.Equal(0L, Money.Zero.MinorUnits);
        Assert.True(Money.Zero.IsZero);
        Assert.False(Money.Zero.IsNegative);
        Assert.False(Money.Zero.IsPositive);
        Assert.Equal(Money.Zero, Money.FromMajorUnits(0m));
        Assert.Equal(Money.Zero, Money.FromMinorUnits(0));
    }

    [Fact]
    public void Min_max_and_clamp_behave_on_signed_amounts()
    {
        Money small = Money.FromMinorUnits(10);
        Money large = Money.FromMinorUnits(20);

        Assert.Equal(small, small.Min(large));
        Assert.Equal(large, small.Max(large));
        Assert.Equal(Money.Zero, Money.FromMinorUnits(-5).AtLeastZero());
        Assert.Equal(large, large.AtLeastZero());
    }

    [Fact]
    public void Equality_is_by_piastre_value()
    {
        Assert.Equal(Money.FromMajorUnits(1.50m), Money.FromMinorUnits(150));
        Assert.NotEqual(Money.FromMajorUnits(1.50m), Money.FromMinorUnits(151));
        Assert.True(Money.FromMajorUnits(1.50m) == Money.FromMinorUnits(150));
        Assert.True(Money.FromMajorUnits(1.50m) != Money.FromMinorUnits(151));
        Assert.Equal(
            Money.FromMajorUnits(1.50m).GetHashCode(),
            Money.FromMinorUnits(150).GetHashCode());
    }

    [Fact]
    public void Display_formatting_always_shows_two_decimals_and_a_currency()
    {
        Assert.Equal("EGP 12.34", Money.FromMajorUnits(12.34m).ToDisplayString());
        Assert.Equal("EGP 0.00", Money.Zero.ToDisplayString());
        Assert.Equal("EGP 0.05", Money.FromMinorUnits(5).ToDisplayString());
        Assert.Equal("-EGP 0.05", Money.FromMinorUnits(-5).ToDisplayString());
        Assert.Equal("-EGP 12.34", Money.FromMajorUnits(-12.34m).ToDisplayString());
        Assert.Equal("EGP 1.00", Money.FromMajorUnits(1m).ToDisplayString());
    }

    [Fact]
    public void Max_piastre_amount_round_trips()
    {
        Money max = Money.FromMinorUnits(long.MaxValue);

        Assert.Equal(long.MaxValue, max.MinorUnits);
        Assert.Equal(long.MaxValue, Money.FromMajorUnits(max.MajorUnits).MinorUnits);
    }
}
