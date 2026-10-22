using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(id => id.Value, value => new BookingId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(b => b.FamilyId)
            .HasConversion(id => id.Value, value => new FamilyId(value))
            .HasColumnName("family_id")
            .IsRequired();

        builder.Property(b => b.CreatedByUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(b => b.ElderlyId)
            .HasConversion(id => id.Value, value => new ElderlyId(value))
            .HasColumnName("elderly_id")
            .IsRequired();

        builder.Property(b => b.CaregiverId)
            .HasConversion(id => id.Value, value => new CaregiverId(value))
            .HasColumnName("caregiver_id")
            .IsRequired();

        builder.Property(b => b.CaregiverType)
            .HasColumnName("caregiver_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(b => b.ShiftType)
            .HasColumnName("shift_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(b => b.BookingDate)
            .HasColumnName("booking_date")
            .IsRequired();

        builder.Property(b => b.StartTime)
            .HasColumnName("start_time")
            .IsRequired();

        builder.Property(b => b.EndTime)
            .HasColumnName("end_time")
            .IsRequired();

        builder.Property(b => b.AcceptanceDeadlineUtc)
            .HasColumnName("acceptance_deadline_utc")
            .IsRequired();

        builder.Property(b => b.ExpiredOnUtc)
            .HasColumnName("expired_on_utc");

        builder.Property(b => b.ServiceAddress)
            .HasColumnName("service_address")
            .HasMaxLength(Booking.MaximumAddressLength)
            .IsRequired();

        builder.Property(b => b.SpecialInstructions)
            .HasColumnName("special_instructions")
            .HasMaxLength(Booking.MaximumInstructionsLength);

        builder.Property(b => b.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(b => b.PaymobOrderId)
            .HasColumnName("paymob_order_id")
            .HasMaxLength(100);

        builder.Property(b => b.PaymobTransactionId)
            .HasColumnName("paymob_transaction_id")
            .HasMaxLength(100);

        builder.Property(b => b.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(Booking.MaximumReasonLength);

        builder.Property(b => b.CaregiverNotes)
            .HasColumnName("caregiver_notes")
            .HasMaxLength(Booking.MaximumNotesLength);

        builder.Property(b => b.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(b => b.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired();
        builder.Property(b => b.PaidOnUtc).HasColumnName("paid_on_utc");
        builder.Property(b => b.ConfirmedOnUtc).HasColumnName("confirmed_on_utc");
        builder.Property(b => b.StartedOnUtc).HasColumnName("started_on_utc");
        builder.Property(b => b.CompletedOnUtc).HasColumnName("completed_on_utc");
        builder.Property(b => b.CancelledOnUtc).HasColumnName("cancelled_on_utc");
        builder.Property(b => b.RefundedOnUtc).HasColumnName("refunded_on_utc");

        builder.Property(b => b.PaymobRefundTransactionId)
            .HasColumnName("paymob_refund_transaction_id")
            .HasMaxLength(100);

        // Refund policy revision preserved at booking time (Owner decisions #7, #8, clause 12).
        // Legacy revision 1 is the column default, so existing rows keep the policy they were sold
        // under without a backfill that could reprice them.
        builder.Property(b => b.RefundPolicyVersion)
            .HasColumnName("refund_policy_version")
            .HasDefaultValue(BookingRefundPolicyVersion.LegacyVersion)
            .IsRequired();

        builder.Property(b => b.RefundPolicyDocumentEffectiveOn)
            .HasColumnName("refund_policy_document_effective_on")
            .HasColumnType("date")
            .HasDefaultValue(default(DateOnly))
            .IsRequired();

        // Money already refunded against this booking, in piastres. Advances only through
        // Booking.RecordRefundedAmount; the refundable ceiling is captured money less this value.
        builder.Property(b => b.RefundedMinorUnits)
            .HasColumnName("refunded_minor_units")
            .HasDefaultValue(0L)
            .IsRequired();

        // Decision #17: an unconfirmed provider refund outcome is recorded on the booking and
        // held there until an operator reconciles it. The whole marker block is additive and
        // defaults to "no ambiguity", so existing rows keep their behaviour untouched.
        builder.Property(b => b.IsRefundOutcomeAmbiguous)
            .HasColumnName("is_refund_outcome_ambiguous")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(b => b.RefundAmbiguityReference)
            .HasColumnName("refund_ambiguity_reference")
            .HasMaxLength(100);

        builder.Property(b => b.RefundAmbiguityReason)
            .HasColumnName("refund_ambiguity_reason")
            .HasMaxLength(64);

        builder.Property(b => b.RefundAmbiguityRaisedOnUtc)
            .HasColumnName("refund_ambiguity_raised_on_utc");

        builder.Property(b => b.RefundAmbiguityAwardMinorUnits)
            .HasColumnName("refund_ambiguity_award_minor_units");

        builder.Property(b => b.RefundAmbiguityClearedOnUtc)
            .HasColumnName("refund_ambiguity_cleared_on_utc");

        builder.Property(b => b.RefundAmbiguityClearedBy)
            .HasColumnName("refund_ambiguity_cleared_by")
            .HasMaxLength(300);

        // Refund-attempt claim (Owner decision #17 hardening). The claim is committed *before* any
        // external refund call, so an interrupted attempt leaves the exact award, its correlation
        // id and its in-flight flag on the record instead of only in memory. RefundAttemptVersion
        // is the optimistic concurrency token (the same pattern as
        // FamilySubscriptionConfiguration.LifecycleVersion): a competing request holding a stale
        // value fails its own commit at the database boundary and therefore never calls the
        // provider.
        builder.Property(b => b.RefundAttemptVersion)
            .HasColumnName("refund_attempt_version")
            .HasDefaultValue(0)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(b => b.RefundAttemptClaimId)
            .HasColumnName("refund_attempt_claim_id");

        builder.Property(b => b.RefundAttemptClaimedOnUtc)
            .HasColumnName("refund_attempt_claimed_on_utc");

        builder.Property(b => b.RefundAttemptAwardMinorUnits)
            .HasColumnName("refund_attempt_award_minor_units");

        builder.Property(b => b.IsRefundAttemptInFlight)
            .HasColumnName("is_refund_attempt_in_flight")
            .HasDefaultValue(false)
            .IsRequired();

        // Per-session allocations of a discounted multi-session package (Owner decision #5).
        // Owned by the booking; the sum of the allocated amounts equals the paid package total,
        // enforced by BookingRefundPolicy.AllocatePackageSessions at purchase.
        builder.OwnsMany(b => b.SessionAllocations, allocation =>
        {
            allocation.ToTable("booking_session_allocations");
            allocation.WithOwner().HasForeignKey("BookingId");
            allocation.Property<BookingId>("BookingId")
                .HasConversion(id => id.Value, value => new BookingId(value))
                .HasColumnName("booking_id")
                .IsRequired();
            allocation.Property(a => a.SessionIndex)
                .HasColumnName("session_index")
                .IsRequired()
                // The index is a domain-assigned dense 1..N slice of the paid
                // package, never a store-generated sequence; without this the
                // EF 10 convention silently marks the composite-key column as
                // identity-by-default and the model drifts from its migration.
                .ValueGeneratedNever();
            allocation.HasKey("BookingId", "SessionIndex");

            allocation.Property(a => a.AllocatedMinorUnits)
                .HasColumnName("allocated_minor_units")
                .IsRequired();

            allocation.Property(a => a.Outcome)
                .HasColumnName("outcome")
                .HasConversion<int>()
                .IsRequired();

            allocation.Property(a => a.DeliveredOnUtc)
                .HasColumnName("delivered_on_utc");

            // Database-level shape guards, mirroring the persisted enum-space checks the W2
            // slice established for its own additive columns: the outcome must be a real
            // BookingSessionOutcome member and an allocation can never be a negative slice of
            // the paid package price. The domain validates both, so these only catch a writer
            // that bypassed the aggregate.
            allocation.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_booking_session_allocations_outcome_range",
                    "outcome BETWEEN 1 AND 4");
                table.HasCheckConstraint(
                    "ck_booking_session_allocations_allocated_non_negative",
                    "allocated_minor_units >= 0");
            });
        });

        // Chat Plus immutable pricing attribution (nullable for legacy
        // bookings, which contribute no sample).
        builder.Property(b => b.BookingAreaId)
            .HasColumnName("booking_area_id")
            .IsRequired(false);
        builder.Property(b => b.BookingServiceId)
            .HasColumnName("booking_service_id")
            .IsRequired(false);
        builder.HasIndex(b => new { b.BookingAreaId, b.BookingServiceId })
            .HasDatabaseName("IX_Bookings_BookingAreaId_BookingServiceId");

        builder.OwnsMany(b => b.PaymentTransactions, transaction =>
        {
            transaction.ToTable("payment_transactions");
            transaction.WithOwner().HasForeignKey("BookingId");
            transaction.Property<BookingId>("BookingId")
                .HasConversion(id => id.Value, value => new BookingId(value))
                .HasColumnName("booking_id")
                .IsRequired();
            transaction.Property(t => t.Id)
                .HasConversion(id => id.Value, value => new PaymentTransactionId(value))
                .HasColumnName("id")
                .IsRequired();
            transaction.HasKey("BookingId", "Id");

            transaction.Property(t => t.PaymobOrderId)
                .HasColumnName("paymob_order_id")
                .HasMaxLength(100)
                .IsRequired();
            transaction.Property(t => t.PaymobTransactionId)
                .HasColumnName("paymob_transaction_id")
                .HasMaxLength(100);
            transaction.Property(t => t.Method)
                .HasColumnName("method")
                .HasConversion<int>()
                .IsRequired();
            transaction.Property(t => t.Amount)
                .HasColumnName("amount")
                .HasPrecision(12, 2)
                .IsRequired();
            transaction.Property(t => t.Currency)
                .HasColumnName("currency")
                .HasMaxLength(10)
                .IsRequired();
            transaction.Property(t => t.Status)
                .HasColumnName("status")
                .HasConversion<int>()
                .IsRequired();
            transaction.Property(t => t.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
            transaction.Property(t => t.SettledOnUtc).HasColumnName("settled_on_utc");
            transaction.Property(t => t.FailedOnUtc).HasColumnName("failed_on_utc");
            transaction.Property(t => t.RefundedOnUtc).HasColumnName("refunded_on_utc");

            transaction.HasIndex(t => t.PaymobOrderId);
            transaction.HasIndex(t => t.PaymobTransactionId);
        });

        builder.OwnsOne(b => b.PriceSnapshot, snapshot =>
        {
            snapshot.Property(p => p.BaseCaregiverFee)
                .HasColumnName("price_base_fee")
                .HasPrecision(12, 2)
                .IsRequired();
            snapshot.Property(p => p.PlatformFeePercentage)
                .HasColumnName("price_platform_fee_percentage")
                .HasPrecision(5, 2)
                .IsRequired();
            snapshot.Property(p => p.PlatformFeeAmount)
                .HasColumnName("price_platform_fee_amount")
                .HasPrecision(12, 2)
                .IsRequired();
            snapshot.Property(p => p.TaxRatePercentage).HasColumnName("price_tax_rate_percentage").HasPrecision(5, 2).IsRequired();
            snapshot.Property(p => p.TaxAmount).HasColumnName("price_tax_amount").HasPrecision(12, 2).IsRequired();
            snapshot.Property(p => p.PlatformChargeRuleVersion).HasColumnName("price_platform_charge_rule_version");
            snapshot.Property(p => p.TotalPayableAmount)
                .HasColumnName("price_total_payable_amount")
                .HasPrecision(12, 2)
                .IsRequired();
            snapshot.Property(p => p.Currency)
                .HasColumnName("price_currency")
                .HasMaxLength(10)
                .IsRequired();
        });

        builder.HasIndex(b => b.FamilyId);
        builder.HasIndex(b => b.CaregiverId);
        builder.HasIndex(b => b.ElderlyId);
        builder.HasIndex(b => b.Status);
    }
}
