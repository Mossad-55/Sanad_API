using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Reviewed quality-complaint cases (clause 5, Owner decisions #11-#14). One
/// case per refund case, so the review and the ledger it produced are correlated
/// by <c>refund_case_id</c> without a cross-context foreign key (W3 design pin
/// item 2: correlation is by plain Guid columns, never a relationship across
/// DbContexts).
/// </summary>
public sealed class ComplaintReviewConfiguration : IEntityTypeConfiguration<ComplaintReview>
{
    public void Configure(EntityTypeBuilder<ComplaintReview> builder)
    {
        builder.ToTable("complaint_reviews", table =>
        {
            table.HasCheckConstraint(
                "ck_complaint_reviews_state",
                "state IN (1, 2, 3, 4)");
            table.HasCheckConstraint(
                "ck_complaint_reviews_timeliness",
                "timeliness IN (1, 2, 3)");
            table.HasCheckConstraint(
                "ck_complaint_reviews_reporting_party",
                "reporting_party IN (1, 2, 3, 4)");
            // A resolved case must carry its SuperAdmin approval; a refused one
            // must carry its deciding administrator (decision #12). Neither may
            // be recorded without an attributable actor (#14).
            table.HasCheckConstraint(
                "ck_complaint_reviews_resolution_attributable",
                "(state <> 3 OR (approved_by_user_id IS NOT NULL AND approved_on_utc IS NOT NULL)) AND " +
                "(state <> 4 OR (refused_by_user_id IS NOT NULL AND refused_on_utc IS NOT NULL))");
            // Approval and refusal are mutually exclusive outcomes.
            table.HasCheckConstraint(
                "ck_complaint_reviews_single_verdict",
                "NOT (approved_on_utc IS NOT NULL AND refused_on_utc IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.RefundCaseId).HasColumnName("refund_case_id").IsRequired();
        builder.Property(x => x.FamilyId).HasColumnName("family_id").IsRequired();
        builder.Property(x => x.ReportingParty).HasColumnName("reporting_party").HasConversion<int>().IsRequired();
        builder.Property(x => x.ReportedByUserId).HasColumnName("reported_by_user_id").IsRequired();
        builder.Property(x => x.ReportedOnUtc).HasColumnName("reported_on_utc").IsRequired();
        builder.Property(x => x.VisitedOnUtc).HasColumnName("visited_on_utc");
        builder.Property(x => x.Timeliness).HasColumnName("timeliness").HasConversion<int>().IsRequired();
        // Lost-update protection (credit-balance precedent, #15): two concurrent
        // reviewers cannot both move a case to a verdict. The loser gets
        // DbUpdateConcurrencyException, mapped to Finance.Complaint.Conflict.
        // State is the existing column, marked as the token — no new column.
        builder.Property(x => x.State).HasColumnName("state").HasConversion<int>().IsConcurrencyToken().IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(ComplaintReview.MaximumNoteLength);
        builder.Property(x => x.InvestigatorUserId).HasColumnName("investigator_user_id");
        builder.Property(x => x.InvestigationStartedOnUtc).HasColumnName("investigation_started_on_utc");
        builder.Property(x => x.EvidenceReference)
            .HasColumnName("evidence_reference")
            .HasMaxLength(ComplaintReview.MaximumEvidenceReferenceLength);
        builder.Property(x => x.DecisionNote).HasColumnName("decision_note").HasMaxLength(ComplaintReview.MaximumNoteLength);
        builder.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(x => x.ApprovedOnUtc).HasColumnName("approved_on_utc");
        builder.Property(x => x.RefusedByUserId).HasColumnName("refused_by_user_id");
        builder.Property(x => x.RefusedOnUtc).HasColumnName("refused_on_utc");
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired();

        // One review per refund case: the review and its money movement are the
        // same case, never two.
        builder.HasIndex(x => x.RefundCaseId)
            .IsUnique()
            .HasDatabaseName("ux_complaint_reviews_refund_case_id");
        // Case work queues read by phase, and the family's own cases by family.
        builder.HasIndex(x => new { x.State, x.ReportedOnUtc })
            .HasDatabaseName("ix_complaint_reviews_state_reported_on_utc");
        builder.HasIndex(x => new { x.FamilyId, x.ReportedOnUtc })
            .HasDatabaseName("ix_complaint_reviews_family_reported_on_utc");

        builder.Ignore(x => x.DomainEvents);
        builder.Ignore(x => x.IsUnderInvestigation);
        builder.Ignore(x => x.ProcessingDeadlineStartsOnUtc);
    }
}
