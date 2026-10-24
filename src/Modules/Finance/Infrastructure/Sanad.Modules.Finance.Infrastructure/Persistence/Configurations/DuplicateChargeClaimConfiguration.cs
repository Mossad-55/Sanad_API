using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Persistence for one family-submitted duplicate-charge support claim, with
/// its decision and execution audit.
/// <para>
/// Serialized on three verified locks (in order of authority):
/// (1) the claim row's own <c>version</c> optimistic token — only one verdict or
/// execution outcome can commit; (2) the refund CASE the claim derives from —
/// the frozen recorder refuses a second movement under it, whatever channel
/// created the first; (3) the capture budget's <c>payment_attempt_id</c> row —
/// the shared serialization point that keeps duplicate-charge, shutdown and
/// legacy awards from ever co-claiming one capture. The active-claim partial
/// unique index below is local admission control only (one LIVE claim per
/// family+attempt); it is not a money guard.
/// </para>
/// <para>
/// The partial scope of that index is the restored R1 rule: at most one
/// non-rejected (live) claim per family+attempt, while a rejected claim keeps
/// its row as readable history WITHOUT keeping its slot. A family's recourse
/// after rejection is a fresh submission with a distinct idempotency key and
/// evidence; the case id and capture budget still guarantee that one capture
/// funds at most one award no matter how many rejected claims accumulate.
/// </para>
/// </summary>
public sealed class DuplicateChargeClaimConfiguration : IEntityTypeConfiguration<DuplicateChargeClaim>
{
    public void Configure(EntityTypeBuilder<DuplicateChargeClaim> builder)
    {
        builder.ToTable("duplicate_charge_claims", table =>
        {
            table.HasCheckConstraint(
                "ck_duplicate_charge_claims_state",
                "state IN (1, 2, 3)");
            table.HasCheckConstraint(
                "ck_duplicate_charge_claims_execution_state",
                "execution_state IN (1, 2, 3, 4, 5, 6)");
            table.HasCheckConstraint(
                "ck_duplicate_charge_claims_award",
                "award_minor_units > 0");
            table.HasCheckConstraint(
                "ck_duplicate_charge_claims_attempt_count",
                "execution_attempt_count >= 0");
            table.HasCheckConstraint(
                "ck_duplicate_charge_claims_version",
                "version > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.FamilyAccountId).HasColumnName("family_account_id").IsRequired();
        builder.Property(x => x.SubscriptionId).HasColumnName("subscription_id").IsRequired();
        builder.Property(x => x.PaymentAttemptId).HasColumnName("payment_attempt_id").IsRequired();
        builder.Property(x => x.RefundCaseId).HasColumnName("refund_case_id").IsRequired();
        builder.Property(x => x.AwardMinorUnits).HasColumnName("award_minor_units").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(DuplicateChargeClaim.MaximumIdempotencyKeyLength);
        builder.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(DuplicateChargeClaim.MaximumEvidenceReferenceLength);
        builder.Property(x => x.CustomerReportedOnUtc).HasColumnName("customer_reported_on_utc").IsRequired();
        builder.Property(x => x.SubmittedOnUtc).HasColumnName("submitted_on_utc").IsRequired();
        builder.Property(x => x.State).HasColumnName("state").HasConversion<int>().IsRequired();
        builder.Property(x => x.ExecutionState).HasColumnName("execution_state").HasConversion<int>().IsRequired();
        builder.Property(x => x.DecidedByAccountId).HasColumnName("decided_by_account_id");
        builder.Property(x => x.DecidedByAccountType).HasColumnName("decided_by_account_type").HasMaxLength(DuplicateChargeClaim.MaximumAccountTypeLength);
        builder.Property(x => x.DecidedOnUtc).HasColumnName("decided_on_utc");
        builder.Property(x => x.FamilyReason).HasColumnName("family_reason").HasMaxLength(DuplicateChargeClaim.MaximumFamilyReasonLength);
        builder.Property(x => x.DecisionNote).HasColumnName("decision_note").HasMaxLength(DuplicateChargeClaim.MaximumDecisionNoteLength);
        builder.Property(x => x.VerifiedBasis).HasColumnName("verified_basis").HasMaxLength(DuplicateChargeClaim.MaximumVerifiedBasisLength);
        builder.Property(x => x.ExecutionAttemptId).HasColumnName("execution_attempt_id");
        builder.Property(x => x.ExecutedByAccountId).HasColumnName("executed_by_account_id");
        builder.Property(x => x.ExecutionStartedOnUtc).HasColumnName("execution_started_on_utc");
        builder.Property(x => x.ExecutionAwardMinorUnits).HasColumnName("execution_award_minor_units");
        builder.Property(x => x.GatewayErrorCode).HasColumnName("gateway_error_code").HasMaxLength(DuplicateChargeClaim.MaximumGatewayErrorCodeLength);
        builder.Property(x => x.ProviderRefundId).HasColumnName("provider_refund_id").HasMaxLength(DuplicateChargeClaim.MaximumProviderRefundIdLength);
        builder.Property(x => x.NotMovedProviderEvidenceReference).HasColumnName("not_moved_provider_evidence_reference").HasMaxLength(DuplicateChargeClaim.MaximumNotMovedEvidenceLength);
        builder.Property(x => x.CompletedOnUtc).HasColumnName("completed_on_utc");
        builder.Property(x => x.ExecutionAttemptCount).HasColumnName("execution_attempt_count").IsRequired();
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired();
        // The verdict/execution serialization token: two concurrent decisions that
        // both read the same claim cannot both commit (platform-control precedent).
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        // Active-claim admission control (restored R1 rule): at most one
        // LIVE (non-rejected) claim per (family, attempt). A rejected claim
        // stays on record and readable but does NOT keep its slot, so a
        // family can resubmit with a distinct key and new evidence after a
        // rejection. Advisory on purpose — the money guards live on the case
        // id, the budget row and the version token, so no count of rejected
        // claims can ever widen a capture.
        builder.HasIndex(x => new { x.FamilyAccountId, x.PaymentAttemptId })
            .IsUnique()
            .HasFilter("\"state\" <> 2")
            .HasDatabaseName("ux_duplicate_charge_claims_family_attempt_active");
        // Replay arbitration: one textual key per family, scoped like the
        // subscription refund request marker. No filter, matching that
        // precedent: PostgreSQL unique indexes treat NULL keys as distinct, so
        // keyless submissions never collide.
        builder.HasIndex(x => new { x.FamilyAccountId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_duplicate_charge_claims_family_idempotency_key");
        // Support Investigation surface: every refund case reachable from its
        // derived correlation id, and admin list filters.
        builder.HasIndex(x => x.RefundCaseId)
            .HasDatabaseName("ix_duplicate_charge_claims_refund_case_id");
        builder.HasIndex(x => x.PaymentAttemptId)
            .HasDatabaseName("ix_duplicate_charge_claims_payment_attempt_id");
        builder.HasIndex(x => x.State)
            .HasDatabaseName("ix_duplicate_charge_claims_state");
    }
}
