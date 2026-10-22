using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Rentals.Domain;

namespace Sanad.Modules.Rentals.Infrastructure.Persistence.Configurations;

/// <summary>
/// A bounded rentable equipment item (clause 8, Owner decision #22). Money
/// columns carry the EGP major-unit <c>numeric(18,2)</c> convention (Owner
/// decision #37) while the domain keeps exact piastre integers; the check
/// constraints backstop the aggregate's rate/deposit/cap invariants at the
/// database level.
/// </summary>
public sealed class RentalEquipmentConfiguration : IEntityTypeConfiguration<RentalEquipment>
{
    public void Configure(EntityTypeBuilder<RentalEquipment> builder)
    {
        builder.ToTable("rental_equipment", table =>
        {
            table.HasCheckConstraint("ck_rental_equipment_daily_rate_positive", "daily_rate > 0");
            table.HasCheckConstraint("ck_rental_equipment_deposit_non_negative", "deposit_amount >= 0");
            table.HasCheckConstraint(
                "ck_rental_equipment_maximum_rental_days",
                "maximum_rental_days >= 1 AND maximum_rental_days <= 365");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.NameEnglish).HasColumnName("name_english").HasMaxLength(RentalEquipment.MaximumNameLength).IsRequired();
        builder.Property(x => x.NameArabic).HasColumnName("name_arabic").HasMaxLength(RentalEquipment.MaximumNameLength).IsRequired();
        builder.Property(x => x.DescriptionEnglish).HasColumnName("description_english").HasMaxLength(RentalEquipment.MaximumDescriptionLength);
        builder.Property(x => x.DescriptionArabic).HasColumnName("description_arabic").HasMaxLength(RentalEquipment.MaximumDescriptionLength);
        RentalMoneyColumns.UseMoneyMajorUnits(builder.Property(x => x.DailyRateMinorUnits), "daily_rate");
        RentalMoneyColumns.UseMoneyMajorUnits(builder.Property(x => x.DepositMinorUnits), "deposit_amount");
        builder.Property(x => x.MaximumRentalDays).HasColumnName("maximum_rental_days").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired();
        builder.Ignore(x => x.DomainEvents);
    }
}
