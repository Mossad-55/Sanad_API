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
        b.Property(x => x.RefundClaimedOnUtc).HasColumnName("refund_claimed_on_utc");
        b.Property(x => x.EarliestArrivalUtc).HasColumnName("earliest_arrival_utc");
        b.Property(x => x.PaymentIntentClaimedOnUtc).HasColumnName("payment_intent_claimed_on_utc");
        b.Property(x => x.PaymentIntentMethod).HasColumnName("payment_intent_method");
        b.Property(x => x.PaymobOrderId).HasColumnName("paymob_order_id");
        b.Property(x => x.IntentionOrderId).HasColumnName("intention_order_id");
        b.Property(x => x.PaymentClientSecret).HasColumnName("payment_client_secret");
        b.Property(x => x.PaymentPublicKey).HasColumnName("payment_public_key");
        b.HasOne<CareHomeFacility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Cascade);
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
