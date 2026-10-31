using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.Modules.Finance.Domain;

namespace Ahlena.Modules.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// The M7 idempotency marker for subscription refund requests (W3 design pin
/// item 4: "callers pass idempotency keys; Finance-side handlers enforce
/// uniqueness via column-level unique constraints (the established
/// IdempotencyKey mechanism), never a receipts table").
/// <para>
/// The unique <c>(family_id, idempotency_key)</c> index is the race boundary:
/// two concurrent requests carrying the same key cannot both append a ledger
/// movement, and the loser observes a unique violation that the application
/// seam maps to a retryable conflict.
/// </para>
/// </summary>
public sealed class SubscriptionRefundRequestConfiguration : IEntityTypeConfiguration<SubscriptionRefundRequest>
{
    public void Configure(EntityTypeBuilder<SubscriptionRefundRequest> builder)
    {
        builder.ToTable("subscription_refund_requests", table =>
        {
            table.HasCheckConstraint("ck_subscription_refund_requests_reason", "reason IN (1, 2, 3)");
            table.HasCheckConstraint("ck_subscription_refund_requests_amount", "requested_amount_minor_units >= 0");
            table.HasCheckConstraint(
                "ck_subscription_refund_requests_key_length",
                $"char_length(idempotency_key) BETWEEN 1 AND {SubscriptionRefundRequest.MaximumIdempotencyKeyLength}");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.RefundCaseId).HasColumnName("refund_case_id").IsRequired();
        builder.Property(x => x.FamilyId).HasColumnName("family_id").IsRequired();
        // The shared refundable-budget correlation (the subscription). Indexed, not
        // unique: one subscription legitimately owns several refund cases.
        builder.Property(x => x.SubscriptionId).HasColumnName("subscription_id").IsRequired();
        builder.Property(x => x.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(SubscriptionRefundRequest.MaximumIdempotencyKeyLength)
            .IsRequired();
        // The requested award is stored in piastres, like every other staging
        // value in the money core; the ledger itself carries the persisted
        // numeric(18,2) EGP movement (Owner decision #37).
        builder.Property(x => x.RequestedAmountMinorUnits).HasColumnName("requested_amount_minor_units").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasConversion<int>().IsRequired();
        builder.Property(x => x.EvidenceReference)
            .HasColumnName("evidence_reference")
            .HasMaxLength(SubscriptionRefundRequest.MaximumEvidenceReferenceLength);
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();

        builder.HasIndex(x => new { x.FamilyId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_subscription_refund_requests_family_idempotency_key");
        builder.HasIndex(x => x.RefundCaseId)
            .IsUnique()
            .HasDatabaseName("ux_subscription_refund_requests_refund_case_id");
        builder.HasIndex(x => x.SubscriptionId)
            .HasDatabaseName("ix_subscription_refund_requests_subscription_id");
    }
}
