using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Rentals.Domain;

namespace Sanad.Modules.Rentals.Infrastructure.Persistence.Configurations;

/// <summary>
/// One recorded refund movement of a booking — the purchasable owner's own
/// outcome row (W3 design pin item 3). The unique
/// <c>(rental_booking_id, kind)</c> index is the race boundary: at most one
/// movement of a kind per booking can ever exist, so a retried refund replay
/// finds the original row or loses the insert to the unique violation. The
/// <c>refund_case_id</c> correlation id is a plain Guid column pointing at
/// the Finance ledger case — never a cross-DbContext foreign key.
/// </summary>
public sealed class RentalRefundMovementConfiguration : IEntityTypeConfiguration<RentalRefundMovement>
{
    public void Configure(EntityTypeBuilder<RentalRefundMovement> builder)
    {
        builder.ToTable("rental_refund_movements", table =>
        {
            table.HasCheckConstraint("ck_rental_refund_movements_kind", "kind IN (1, 2, 3)");
            table.HasCheckConstraint("ck_rental_refund_movements_destination", "destination IN (1, 2, 3, 4)");
            table.HasCheckConstraint("ck_rental_refund_movements_amount_non_negative", "amount >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.RentalBookingId).HasColumnName("rental_booking_id").IsRequired();
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.RefundCaseId).HasColumnName("refund_case_id").IsRequired();
        builder.Property(x => x.Destination).HasColumnName("destination").HasConversion<int>().IsRequired();
        RentalMoneyColumns.UseMoneyMajorUnits(builder.Property(x => x.AmountMinorUnits), "amount");
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id").IsRequired();
        builder.Property(x => x.GatewayReference).HasColumnName("gateway_reference").HasMaxLength(RentalRefundMovement.MaximumGatewayReferenceLength);
        builder.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(RentalRefundMovement.MaximumEvidenceReferenceLength);
        builder.Property(x => x.RecordedOnUtc).HasColumnName("recorded_on_utc").IsRequired();
        builder.HasIndex(x => new { x.RentalBookingId, x.Kind })
            .IsUnique()
            .HasDatabaseName("ux_rental_refund_movements_booking_kind");
        builder.HasIndex(x => x.RefundCaseId).HasDatabaseName("ix_rental_refund_movements_refund_case_id");
    }
}
