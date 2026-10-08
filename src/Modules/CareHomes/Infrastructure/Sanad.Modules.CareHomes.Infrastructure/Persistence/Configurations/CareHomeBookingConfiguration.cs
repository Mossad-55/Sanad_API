using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Configurations;

public sealed class CareHomeBookingConfiguration : IEntityTypeConfiguration<CareHomeBooking>
{
    public void Configure(EntityTypeBuilder<CareHomeBooking> b)
    {
        b.ToTable("bookings"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.FacilityId).HasConversion(x => x.Value, x => new CareHomeId(x)).IsRequired();
        b.Property(x => x.FamilyUserId).HasConversion(x => x.Value, x => new UserId(x)).IsRequired();
        b.Property(x => x.FamilyId).HasConversion(x => x.Value, x => new FamilyId(x)).IsRequired();
        b.Property(x => x.ElderlyId).HasConversion(x => x.Value, x => new ElderlyId(x)).IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired(); b.Property(x => x.PaymentStatus).HasConversion<int>().IsRequired();
        b.Property(x => x.BaseAmount).HasPrecision(18, 2); b.Property(x => x.PlatformFeeAmount).HasPrecision(18, 2); b.Property(x => x.TaxAmount).HasPrecision(18, 2); b.Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Property(x => x.MedicalSnapshotJson).HasColumnType("jsonb"); b.Property(x => x.MerchantReference).HasMaxLength(80).IsRequired();
        b.HasIndex(x => x.MerchantReference).IsUnique(); b.HasIndex(x => new { x.FacilityId, x.Status, x.StartDate, x.EndDate });
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.ExtensionOfBookingId).HasColumnName("extension_of_booking_id");
        b.Property(x => x.ExtensionRootBookingId).HasColumnName("extension_root_booking_id");
        b.HasIndex(x => new { x.ExtensionRootBookingId, x.StartDate });
        b.Property(x => x.RefundClaimedOnUtc).HasColumnName("refund_claimed_on_utc");
        b.Property(x => x.RefundStatus).HasConversion<int>().HasColumnName("refund_status").IsRequired();
        b.Property(x => x.RefundAmount).HasColumnName("refund_amount").HasPrecision(18, 2);
        b.Property(x => x.RefundFailureReason).HasColumnName("refund_failure_reason").HasMaxLength(2000);
        b.Property(x => x.RefundCompletedOnUtc).HasColumnName("refund_completed_on_utc");
        b.Property(x => x.RefundCompletedBy).HasConversion(x => x.HasValue ? x.Value.Value : (Guid?)null, x => x.HasValue ? new UserId(x.Value) : (UserId?)null).HasColumnName("refund_completed_by");
        b.Property(x => x.PaymentCompletedOnUtc).HasColumnName("payment_completed_on_utc");
        b.Property(x => x.EarliestArrivalUtc).HasColumnName("earliest_arrival_utc");
        b.Property(x => x.PaymentIntentClaimedOnUtc).HasColumnName("payment_intent_claimed_on_utc");
        b.Property(x => x.PaymentIntentMethod).HasColumnName("payment_intent_method");
        b.Property(x => x.PaymobOrderId).HasColumnName("paymob_order_id");
        b.Property(x => x.IntentionOrderId).HasColumnName("intention_order_id");
        b.Property(x => x.PaymentClientSecret).HasColumnName("payment_client_secret");
        b.Property(x => x.PaymentPublicKey).HasColumnName("payment_public_key");
        b.HasOne<CareHomeFacility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<CareHomeBooking>().WithMany().HasForeignKey(x => x.ExtensionOfBookingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CareHomeBooking>().WithMany().HasForeignKey(x => x.ExtensionRootBookingId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.AssignedRoomId).HasColumnName("assigned_room_id");
        b.Property(x => x.AssignedBedId).HasColumnName("assigned_bed_id");
        b.Property(x => x.ActualCheckInOnUtc).HasColumnName("actual_check_in_on_utc");
        b.Property(x => x.ActualCheckOutOnUtc).HasColumnName("actual_check_out_on_utc");
        b.Property(x => x.ActualCheckInRecordedBy).HasConversion(x => x.HasValue ? x.Value.Value : (Guid?)null, x => x.HasValue ? new UserId(x.Value) : (UserId?)null).HasColumnName("actual_check_in_recorded_by");
        b.Property(x => x.ActualCheckOutRecordedBy).HasConversion(x => x.HasValue ? x.Value.Value : (Guid?)null, x => x.HasValue ? new UserId(x.Value) : (UserId?)null).HasColumnName("actual_check_out_recorded_by");
        b.Property(x => x.FamilyCheckInConfirmedBy).HasConversion(x => x.HasValue ? x.Value.Value : (Guid?)null, x => x.HasValue ? new UserId(x.Value) : (UserId?)null).HasColumnName("family_check_in_confirmed_by");
        b.Property(x => x.FamilyCheckInConfirmedOnUtc).HasColumnName("family_check_in_confirmed_on_utc");
    }
}

public sealed class CareHomeBookingAssignmentHistoryConfiguration : IEntityTypeConfiguration<CareHomeBookingAssignmentHistory>
{
    public void Configure(EntityTypeBuilder<CareHomeBookingAssignmentHistory> b)
    {
        b.ToTable("booking_assignment_history"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired(); b.Property(x => x.FromRoomId).HasColumnName("from_room_id"); b.Property(x => x.FromBedId).HasColumnName("from_bed_id");
        b.Property(x => x.ToRoomId).HasColumnName("to_room_id").IsRequired(); b.Property(x => x.ToBedId).HasColumnName("to_bed_id"); b.Property(x => x.EffectiveDate).HasColumnName("effective_date").IsRequired();
        b.Property(x => x.Actor).HasConversion(x => x.Value, x => new UserId(x)).HasColumnName("actor_user_id").IsRequired(); b.Property(x => x.OccurredOnUtc).HasColumnName("occurred_on_utc").IsRequired();
        b.HasIndex(x => new { x.BookingId, x.EffectiveDate }).IsUnique().HasFilter("\"from_room_id\" IS NOT NULL");
    }
}

public sealed class CareHomeTransferNotificationOutboxConfiguration : IEntityTypeConfiguration<CareHomeTransferNotificationOutbox>
{
    public void Configure(EntityTypeBuilder<CareHomeTransferNotificationOutbox> b)
    {
        b.ToTable("transfer_notification_outbox"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired(); b.Property(x => x.TransferId).HasColumnName("transfer_id").IsRequired(); b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired(); b.Property(x => x.NextAttemptOnUtc).HasColumnName("next_attempt_on_utc").IsRequired(); b.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        b.Property(x => x.LastAttemptOnUtc).HasColumnName("last_attempt_on_utc"); b.Property(x => x.LastError).HasMaxLength(2000); b.Property(x => x.ClaimToken).HasColumnName("claim_token");
        b.HasIndex(x => x.TransferId).IsUnique(); b.HasIndex(x => new { x.Status, x.NextAttemptOnUtc });
    }
}

public sealed class CareHomeNotificationOutboxConfiguration : IEntityTypeConfiguration<CareHomeNotificationOutbox>
{
    public void Configure(EntityTypeBuilder<CareHomeNotificationOutbox> b)
    {
        b.ToTable("booking_notification_outbox"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.EventKey).HasColumnName("event_key").HasMaxLength(180).IsRequired();
        b.Property(x => x.EventType).HasColumnName("event_type").HasConversion<int>().IsRequired();
        b.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        b.Property(x => x.DisputeId).HasColumnName("dispute_id");
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        b.Property(x => x.NextAttemptOnUtc).HasColumnName("next_attempt_on_utc").IsRequired();
        b.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        b.Property(x => x.LastAttemptOnUtc).HasColumnName("last_attempt_on_utc");
        b.Property(x => x.LastError).HasMaxLength(2000);
        b.Property(x => x.ClaimToken).HasColumnName("claim_token");
        b.HasIndex(x => x.EventKey).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptOnUtc });
    }
}
