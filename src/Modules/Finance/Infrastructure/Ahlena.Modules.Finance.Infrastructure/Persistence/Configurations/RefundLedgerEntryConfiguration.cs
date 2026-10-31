using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.Modules.Finance.Domain;

namespace Ahlena.Modules.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Append-only refund ledger. The amount is persisted in EGP major units as
/// <c>numeric(18,2)</c> (Owner decision #37: the standardized Finance money
/// column convention, matching the Families money columns), while the domain
/// value object keeps the exact piastre <see cref="Money.MinorUnits"/> integer.
/// Four decimal places are not needed because the minor unit is the piastre.
/// </summary>
public sealed class RefundLedgerEntryConfiguration : IEntityTypeConfiguration<RefundLedgerEntry>
{
    public void Configure(EntityTypeBuilder<RefundLedgerEntry> builder)
    {
        builder.ToTable("refund_ledger_entries", table =>
        {
            table.HasCheckConstraint(
                "ck_refund_ledger_entries_amount_precision",
                "amount = round(amount, 2)");
            table.HasCheckConstraint(
                "ck_refund_ledger_entries_entry_type",
                "entry_type IN (1, 2, 3, 4, 5, 6, 7, 8)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Sequence)
            .HasColumnName("entry_sequence")
            .ValueGeneratedNever()
            .IsRequired();
        builder.Property(x => x.RefundCaseId).HasColumnName("refund_case_id").IsRequired();
        builder.Property(x => x.EntryType).HasColumnName("entry_type").HasConversion<int>().IsRequired();
        builder.Property(x => x.Amount)
            .HasColumnName("amount")
            .HasPrecision(18, 2)
            .HasConversion(
                money => money.MajorUnits,
                value => Money.FromMajorUnits(value))
            .IsRequired();
        builder.Property(x => x.OccurredOnUtc).HasColumnName("occurred_on_utc").IsRequired();
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(RefundLedgerEntry.MaximumEvidenceReferenceLength);
        builder.Property(x => x.ProviderReference).HasColumnName("provider_reference").HasMaxLength(RefundLedgerEntry.MaximumProviderReferenceLength);
        builder.HasIndex(x => x.RefundCaseId).HasDatabaseName("ix_refund_ledger_entries_refund_case_id");
        builder.HasIndex(x => new { x.RefundCaseId, x.Sequence })
            .IsUnique()
            .HasDatabaseName("ux_refund_ledger_entries_case_sequence");
        builder.HasIndex(x => x.OccurredOnUtc).HasDatabaseName("ix_refund_ledger_entries_occurred_on_utc");
    }
}
