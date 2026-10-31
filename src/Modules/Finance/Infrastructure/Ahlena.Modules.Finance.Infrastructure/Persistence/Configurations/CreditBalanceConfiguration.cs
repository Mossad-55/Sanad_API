using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Finance.Domain;

namespace Ahlena.Modules.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Per-family app credit balance (Owner decision #15). The balance is stored in
/// EGP major units as <c>numeric(18,2)</c> (Owner decision #37: the
/// standardized Finance money-column convention, matching Families), the domain
/// keeps the exact piastre integer and enforces the non-negative invariant, and
/// the check constraint is the database-level backstop for that invariant.
/// </summary>
public sealed class CreditBalanceConfiguration : IEntityTypeConfiguration<CreditBalance>
{
    public void Configure(EntityTypeBuilder<CreditBalance> builder)
    {
        builder.ToTable("credit_balances", table =>
        {
            table.HasCheckConstraint("ck_credit_balances_balance_non_negative", "balance >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.FamilyId)
            .HasConversion(x => x.Value, x => new FamilyId(x))
            .HasColumnName("family_id")
            .IsRequired();
        builder.Property(x => x.Balance)
            .HasColumnName("balance")
            .HasPrecision(18, 2)
            .IsConcurrencyToken()
            .HasConversion(
                money => money.MajorUnits,
                value => Money.FromMajorUnits(value))
            .IsRequired();
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired();
        builder.HasIndex(x => x.FamilyId).IsUnique().HasDatabaseName("ux_credit_balances_family_id");
        builder.Ignore(x => x.DomainEvents);
    }
}
