using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Rentals.Infrastructure.Persistence.Configurations;

/// <summary>
/// The rentals money-column convention (Owner decision #37): money is
/// persisted in EGP major units as <c>numeric(18,2)</c> — matching the Finance
/// and Families money columns — while the domain keeps the exact EGP piastre
/// integer. The conversion is exact at the piastre scale (a decimal scaling by
/// 100 rounds half-to-even through <see cref="Money.FromMajorUnits"/>).
/// </summary>
internal static class RentalMoneyColumns
{
    public static PropertyBuilder<long> UseMoneyMajorUnits(PropertyBuilder<long> property, string columnName) =>
        property
            .HasColumnName(columnName)
            .HasPrecision(18, 2)
            .HasConversion(
                minorUnits => minorUnits / 100m,
                majorUnits => Money.FromMajorUnits(majorUnits).MinorUnits)
            .IsRequired();

    /// <summary>
    /// The optional money column (the captured-total ceiling and the in-flight
    /// pending award are unknown until they exist): <c>numeric(18,2)</c>, null
    /// in the database while unset. EF never passes nulls through value
    /// converters, so only non-null values traverse the conversion.
    /// </summary>
    public static PropertyBuilder<long?> UseOptionalMoneyMajorUnits(PropertyBuilder<long?> property, string columnName) =>
        property
            .HasColumnName(columnName)
            .HasPrecision(18, 2)
            .HasConversion(
                minorUnits => minorUnits.GetValueOrDefault() / 100m,
                majorUnits => Money.FromMajorUnits(majorUnits).MinorUnits);
}
