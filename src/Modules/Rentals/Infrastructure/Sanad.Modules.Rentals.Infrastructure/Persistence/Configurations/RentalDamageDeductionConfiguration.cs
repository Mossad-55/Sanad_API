using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Rentals.Domain;

namespace Sanad.Modules.Rentals.Infrastructure.Persistence.Configurations;

/// <summary>
/// One documented damage deduction at inspection (clause 8, Owner decision
/// #21: append-style — approved at most once, disputed at most once, resolved
/// at most once, never rewritten). The netting decision itself lives on the
/// booking aggregate; this row only ever records evidence.
/// </summary>
public sealed class RentalDamageDeductionConfiguration : IEntityTypeConfiguration<RentalDamageDeduction>
{
    public void Configure(EntityTypeBuilder<RentalDamageDeduction> builder)
    {
        builder.ToTable("rental_damage_deductions", table =>
        {
            table.HasCheckConstraint("ck_rental_damage_deductions_amount_positive", "amount > 0");
            table.HasCheckConstraint("ck_rental_damage_deductions_dispute_state", "dispute_state IN (0, 1, 2)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.RentalBookingId).HasColumnName("rental_booking_id").IsRequired();
        builder.Property(x => x.DescriptionEnglish).HasColumnName("description_english").HasMaxLength(RentalDamageDeduction.MaximumDescriptionLength).IsRequired();
        builder.Property(x => x.DescriptionArabic).HasColumnName("description_arabic").HasMaxLength(RentalDamageDeduction.MaximumDescriptionLength);
        RentalMoneyColumns.UseMoneyMajorUnits(builder.Property(x => x.AmountMinorUnits), "amount");
        builder.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(RentalDamageDeduction.MaximumEvidenceReferenceLength).IsRequired();
        builder.Property(x => x.RecordedByAdminId).HasColumnName("recorded_by_admin_id").IsRequired();
        builder.Property(x => x.RecordedOnUtc).HasColumnName("recorded_on_utc").IsRequired();
        builder.Property(x => x.ApprovedByAdminId).HasColumnName("approved_by_admin_id");
        builder.Property(x => x.ApprovedOnUtc).HasColumnName("approved_on_utc");
        builder.Property(x => x.DisputeState).HasColumnName("dispute_state").HasConversion<int>().IsRequired();
        builder.Property(x => x.DisputedOnUtc).HasColumnName("disputed_on_utc");
        builder.Property(x => x.DisputeReason).HasColumnName("dispute_reason").HasMaxLength(RentalDamageDeduction.MaximumDisputeReasonLength);
        builder.Property(x => x.DisputeResolvedOnUtc).HasColumnName("dispute_resolved_on_utc");
        builder.Property(x => x.DisputeResolvedByAdminId).HasColumnName("dispute_resolved_by_admin_id");
        builder.HasIndex(x => x.RentalBookingId).HasDatabaseName("ix_rental_damage_deductions_rental_booking_id");
    }
}
