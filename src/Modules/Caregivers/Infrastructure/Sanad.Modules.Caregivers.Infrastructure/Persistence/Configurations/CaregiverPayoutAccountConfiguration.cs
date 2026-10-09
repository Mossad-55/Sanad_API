using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Configurations;

public sealed class CaregiverPayoutAccountConfiguration :
    IEntityTypeConfiguration<CaregiverPayoutAccount>
{
    public void Configure(EntityTypeBuilder<CaregiverPayoutAccount> builder)
    {
        builder.ToTable("caregiver_payout_accounts");
        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id)
            .HasConversion(id => id.Value, value => new CaregiverPayoutAccountId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(account => account.CaregiverId)
            .HasConversion(id => id.Value, value => new CaregiverId(value))
            .HasColumnName("caregiver_id")
            .IsRequired();
        builder.HasIndex(account => account.CaregiverId).IsUnique();
        builder.Property(account => account.AccountHolderName)
            .HasColumnName("account_holder_name")
            .HasMaxLength(CaregiverPayoutAccount.MaximumHolderNameLength)
            .IsRequired();
        builder.Property(account => account.BankCode)
            .HasColumnName("bank_code")
            .HasMaxLength(CaregiverPayoutAccount.MaximumBankCodeLength)
            .IsRequired();
        builder.Property(account => account.IbanCiphertext)
            .HasColumnName("iban_ciphertext")
            .HasColumnType("text")
            .IsRequired();
        builder.Property(account => account.IbanLast4)
            .HasColumnName("iban_last4")
            .HasMaxLength(CaregiverPayoutAccount.IbanLast4Length)
            .IsRequired();
        builder.Property(account => account.Revision)
            .HasColumnName("revision")
            .IsRequired();
        builder.Property(account => account.Status)
            .HasColumnName("status")
            .IsRequired();
        builder.Property(account => account.RejectionReason)
            .HasColumnName("rejection_reason")
            .HasMaxLength(500);
        builder.Property(account => account.VerifiedBy)
            .HasConversion(id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null)
            .HasColumnName("verified_by");
        builder.Property(account => account.VerifiedOnUtc)
            .HasColumnName("verified_on_utc");
        builder.Property(account => account.ReviewedBy)
            .HasConversion(id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null)
            .HasColumnName("reviewed_by");
        builder.Property(account => account.ReviewedOnUtc)
            .HasColumnName("reviewed_on_utc");
        builder.Property(account => account.VerificationSource)
            .HasColumnName("verification_source")
            .HasMaxLength(CaregiverPayoutAccount.MaximumSourceLength);
        builder.Property(account => account.Reference)
            .HasColumnName("reference")
            .HasMaxLength(CaregiverPayoutAccount.MaximumReferenceLength);
        builder.Property(account => account.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();
        builder.Property(account => account.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();
        builder.Ignore(account => account.DomainEvents);
    }
}
