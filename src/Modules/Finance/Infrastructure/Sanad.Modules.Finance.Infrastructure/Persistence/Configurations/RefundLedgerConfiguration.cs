using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Refund case root. Append-only in the sense that every state change is
/// recorded as a new <see cref="RefundLedgerEntry"/>; the root carries only the
/// correlation key, so no column here is ever updated.
/// </summary>
public sealed class RefundLedgerConfiguration : IEntityTypeConfiguration<RefundLedger>
{
    public void Configure(EntityTypeBuilder<RefundLedger> builder)
    {
        builder.ToTable("refund_ledgers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.RefundCaseId).HasColumnName("refund_case_id").IsRequired();
        builder.HasIndex(x => x.RefundCaseId).IsUnique().HasDatabaseName("ux_refund_ledgers_refund_case_id");
        builder.Ignore(x => x.DomainEvents);
        builder.HasMany(x => x.Entries)
            .WithOne()
            .HasForeignKey(x => x.RefundCaseId)
            .HasPrincipalKey(x => x.RefundCaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
