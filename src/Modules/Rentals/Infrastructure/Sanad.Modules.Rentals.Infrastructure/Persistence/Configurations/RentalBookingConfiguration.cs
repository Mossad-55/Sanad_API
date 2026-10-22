using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Rentals.Domain;

namespace Sanad.Modules.Rentals.Infrastructure.Persistence.Configurations;

/// <summary>
/// The rental booking aggregate (clause 8 lifecycle). The <c>version</c>
/// column is the optimistic-concurrency token that makes two concurrent
/// refund claims surface as a retryable conflict instead of a second money
/// movement; the <c>pending_refund_*</c> columns persist the claimed Finance
/// refund case (W3 design pin item 3: the correlation id lives on the owner's
/// own rows first). Correlation ids to Families/Identity are plain Guid
/// columns — no cross-DbContext foreign keys.
/// </summary>
public sealed class RentalBookingConfiguration : IEntityTypeConfiguration<RentalBooking>
{
    public void Configure(EntityTypeBuilder<RentalBooking> builder)
    {
        builder.ToTable("rental_bookings", table =>
        {
            table.HasCheckConstraint("ck_rental_bookings_status", "status IN (1, 2, 3, 4, 5, 6)");
            table.HasCheckConstraint("ck_rental_bookings_planned_days", "planned_days >= 1");
            table.HasCheckConstraint("ck_rental_bookings_daily_rate_positive", "daily_rate > 0");
            table.HasCheckConstraint("ck_rental_bookings_deposit_non_negative", "deposit_amount >= 0");
            table.HasCheckConstraint("ck_rental_bookings_pending_refund_kind", "pending_refund_kind IS NULL OR pending_refund_kind IN (1, 2, 3)");
            // The refund claim may only name a destination that carries money
            // (Owner decision: original rails or app credit); every other enum
            // value is a caller bug, not a valid persisted state.
            table.HasCheckConstraint("ck_rental_bookings_pending_refund_destination", "pending_refund_destination IS NULL OR pending_refund_destination IN (1, 2)");
            table.HasCheckConstraint("ck_rental_bookings_pending_refund_amount_non_negative", "pending_refund_amount IS NULL OR pending_refund_amount >= 0");
            table.HasCheckConstraint("ck_rental_bookings_captured_total_non_negative", "captured_total IS NULL OR captured_total >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.EquipmentId).HasColumnName("equipment_id").IsRequired();
        builder.Property(x => x.FamilyId).HasColumnName("family_id").IsRequired();
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id").IsRequired();
        builder.Property(x => x.PlannedDays).HasColumnName("planned_days").IsRequired();
        RentalMoneyColumns.UseMoneyMajorUnits(builder.Property(x => x.DailyRateMinorUnits), "daily_rate");
        RentalMoneyColumns.UseMoneyMajorUnits(builder.Property(x => x.DepositMinorUnits), "deposit_amount");
        builder.Property(x => x.MaximumRentalDaysSnapshot).HasColumnName("maximum_rental_days_snapshot").IsRequired();
        builder.Property(x => x.NameEnglishSnapshot).HasColumnName("name_english_snapshot").HasMaxLength(RentalEquipment.MaximumNameLength).IsRequired();
        builder.Property(x => x.NameArabicSnapshot).HasColumnName("name_arabic_snapshot").HasMaxLength(RentalEquipment.MaximumNameLength).IsRequired();
        builder.Property(x => x.DeliveryAddress).HasColumnName("delivery_address").HasMaxLength(RentalBooking.MaximumAddressLength);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.PendingRefundKind).HasColumnName("pending_refund_kind").HasConversion<int>();
        builder.Property(x => x.PendingRefundDestination).HasColumnName("pending_refund_destination").HasConversion<int>();
        builder.Property(x => x.PendingRefundCaseId).HasColumnName("pending_refund_case_id");
        RentalMoneyColumns.UseOptionalMoneyMajorUnits(builder.Property(x => x.PendingRefundAmountMinorUnits), "pending_refund_amount");
        builder.Property(x => x.TermsSnapshotOnUtc).HasColumnName("terms_snapshot_on_utc").IsRequired();
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired();
        RentalMoneyColumns.UseOptionalMoneyMajorUnits(builder.Property(x => x.CapturedMinorUnits), "captured_total");
        builder.Property(x => x.DeliveredOnUtc).HasColumnName("delivered_on_utc");
        builder.Property(x => x.ReturnedOnUtc).HasColumnName("returned_on_utc");
        builder.Property(x => x.SettlementDeadlineUtc).HasColumnName("settlement_deadline_utc");
        builder.Property(x => x.InspectionCompletedOnUtc).HasColumnName("inspection_completed_on_utc");
        builder.Property(x => x.InspectionCompletedByUserId).HasColumnName("inspection_completed_by_user_id");
        builder.Property(x => x.InspectionNotes).HasColumnName("inspection_notes").HasMaxLength(RentalBooking.MaximumNotesLength);
        builder.Property(x => x.CancelledOnUtc).HasColumnName("cancelled_on_utc");
        builder.Property(x => x.CancelledByUserId).HasColumnName("cancelled_by_user_id");
        builder.Property(x => x.DepositSettledOnUtc).HasColumnName("deposit_settled_on_utc");
        builder.Property(x => x.DepositSettledByUserId).HasColumnName("deposit_settled_by_user_id");
        builder.HasIndex(x => x.FamilyId).HasDatabaseName("ix_rental_bookings_family_id");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_rental_bookings_status");
        builder.HasIndex(x => x.PendingRefundCaseId)
            .IsUnique()
            .HasDatabaseName("ux_rental_bookings_pending_refund_case_id")
            .HasFilter("pending_refund_case_id IS NOT NULL");
        builder.Ignore(x => x.DomainEvents);
        builder.HasMany(x => x.PaymentTransactions)
            .WithOne()
            .HasForeignKey(x => x.RentalBookingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.DamageDeductions)
            .WithOne()
            .HasForeignKey(x => x.RentalBookingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.RefundMovements)
            .WithOne()
            .HasForeignKey(x => x.RentalBookingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
