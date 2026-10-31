using Ahlena.BuildingBlocks.Domain.Exceptions;

namespace Ahlena.Modules.Finance.Domain;

/// <summary>
/// Refund/finance money value object for the money core.
/// <para>
/// Currency is fixed to EGP: the refund policy's minor unit is the piastre
/// (1 EGP = 100 piastres) and the locked financial decisions express every
/// amount in currency minor units (Owner decision #4). The amount is carried as
/// an integral <see cref="long"/> of piastres, so additions, subtractions and
/// proration scaling are exact integer arithmetic and cannot lose precision.
/// </para>
/// <para>
/// A <see cref="Money"/> may be negative, because a signed value is needed to
/// express deltas (for example a ledger entry that reverses a previous award).
/// Callers that must never go negative enforce that at the aggregate boundary
/// (see <c>CreditBalance</c>), not here. Conversion from a decimal major-unit
/// amount rounds half to even (<see cref="MidpointRounding.ToEven"/>), matching
/// the rounding used by the existing platform charge and booking price
/// calculations.
/// </para>
/// </summary>
public sealed class Money : Ahlena.BuildingBlocks.Domain.Abstractions.ValueObject
{
    public const string CurrencyCode = "EGP";
    public const int MinorUnitsPerMajorUnit = 100;
    public const int DecimalPlaces = 2;

    /// <summary>Zero piastres. The proration floor is 0 (Owner decision #31).</summary>
    public static readonly Money Zero = new(0);

    private Money(long minorUnits)
    {
        MinorUnits = minorUnits;
    }

    /// <summary>Signed amount in piastres. This is the persisted/authoritative value.</summary>
    public long MinorUnits { get; }

    public string Currency => CurrencyCode;

    public bool IsZero => MinorUnits == 0;
    public bool IsNegative => MinorUnits < 0;
    public bool IsPositive => MinorUnits > 0;

    /// <summary>Major-unit (EGP) view. Never used for arithmetic; display/persistence only.</summary>
    public decimal MajorUnits => MinorUnits / (decimal)MinorUnitsPerMajorUnit;

    public static Money FromMinorUnits(long minorUnits) => new(minorUnits);

    public static Money FromPiastres(long piastres) => new(piastres);

    /// <summary>Zero-piastre amount — re-exposed as a method for call-site clarity.</summary>
    public static Money ZeroEgp() => Zero;

    /// <summary>
    /// Converts a major-unit amount to piastres. The scale is exact (a decimal
    /// scaling by 100 cannot itself lose precision) and the result rounds half
    /// to even, so an amount that lands exactly on .5 piastre goes to the even
    /// piastre, symmetrically for positive and negative values. The final cast
    /// is range-checked, so an amount outside the <see cref="long"/> piastre
    /// range fails loudly instead of wrapping.
    /// </summary>
    public static Money FromMajorUnits(decimal amount)
    {
        decimal scaled = amount * MinorUnitsPerMajorUnit;
        return new(checked((long)decimal.Round(scaled, 0, MidpointRounding.ToEven)));
    }

    /// <summary>
    /// Proration split: this amount multiplied by <paramref name="numerator"/> /
    /// <paramref name="denominator"/>, rounding half to even on the piastre.
    /// The multiplication is performed in <see cref="decimal"/> before the
    /// divide only because decimal division is the exact rational operation at
    /// this scale; the intermediate product can never overflow
    /// <see cref="decimal"/> for policy-realistic amounts and the final cast is
    /// range-checked.
    /// <para>
    /// There is deliberately no minimum-award floor: a result of exactly zero
    /// piastres is returned as zero (Owner decision #31).
    /// </para>
    /// </summary>
    public Money ScaleBy(long numerator, long denominator)
    {
        if (denominator == 0)
            throw new DomainException("A proration denominator of zero is not a valid ratio.");
        if (numerator == 0 || MinorUnits == 0)
            return Zero;

        decimal scaled = MinorUnits * (decimal)numerator / denominator;
        return new(checked((long)decimal.Round(scaled, 0, MidpointRounding.ToEven)));
    }

    /// <summary>Proration by an already-computed fractional ratio (e.g. unused/paid period).</summary>
    public Money ScaleBy(decimal ratio)
    {
        decimal scaled = MinorUnits * ratio;
        return new(checked((long)decimal.Round(scaled, 0, MidpointRounding.ToEven)));
    }

    public Money Add(Money other) => new(checked(MinorUnits + other.MinorUnits));

    public Money Subtract(Money other) => new(checked(MinorUnits - other.MinorUnits));

    public Money Negate() => new(checked(-MinorUnits));

    public Money Min(Money other) => MinorUnits <= other.MinorUnits ? this : other;

    public Money Max(Money other) => MinorUnits >= other.MinorUnits ? this : other;

    /// <summary>Clamps to zero or more. Used by "cannot go negative" boundaries.</summary>
    public Money AtLeastZero() => IsNegative ? Zero : this;

    public int CompareTo(Money other) => MinorUnits.CompareTo(other.MinorUnits);

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator -(Money value) => value.Negate();

    public static bool operator >(Money left, Money right) => left.MinorUnits > right.MinorUnits;

    public static bool operator <(Money left, Money right) => left.MinorUnits < right.MinorUnits;

    public static bool operator >=(Money left, Money right) => left.MinorUnits >= right.MinorUnits;

    public static bool operator <=(Money left, Money right) => left.MinorUnits <= right.MinorUnits;

    /// <summary>
    /// Invariant display string, e.g. <c>EGP 12.34</c> or <c>-EGP 0.05</c>.
    /// Always two decimal places so a zero award renders as <c>EGP 0.00</c>
    /// rather than a bare <c>0</c>.
    /// </summary>
    public string ToDisplayString()
    {
        long absolute = Math.Abs(MinorUnits);
        string sign = MinorUnits < 0 ? "-" : string.Empty;
        return $"{sign}{CurrencyCode} {absolute / MinorUnitsPerMajorUnit}.{absolute % MinorUnitsPerMajorUnit:D2}";
    }

    public override string ToString() => ToDisplayString();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MinorUnits;
    }
}
