using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Configurations;

public sealed class CaregiverPayoutConfiguration :
    IEntityTypeConfiguration<CaregiverPayout>
{
    public void Configure(EntityTypeBuilder<CaregiverPayout> builder)
    {
        builder.ToTable("caregiver_payouts");
        builder.HasKey(payout => payout.Id);
        builder.Property(payout => payout.Id)
            .HasConversion(id => id.Value, value => new CaregiverPayoutId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(payout => payout.BookingId)
            .HasConversion(id => id.Value, value => new BookingId(value))
            .HasColumnName("booking_id")
            .IsRequired();
        builder.HasIndex(payout => payout.BookingId)
            .IsUnique()
            .HasFilter("\"status\" = 1")
            .HasDatabaseName("ux_caregiver_payouts_booking_paid");
        builder.Property(payout => payout.CaregiverId)
            .HasConversion(id => id.Value, value => new CaregiverId(value))
            .HasColumnName("caregiver_id")
            .IsRequired();
        builder.HasIndex(payout => payout.CaregiverId)
            .HasDatabaseName("ix_caregiver_payouts_caregiver");
        builder.Property(payout => payout.GrossAmount)
            .HasColumnName("gross_amount")
            .HasPrecision(12, 2)
            .IsRequired();
        builder.Property(payout => payout.PlatformFeeAmount)
            .HasColumnName("platform_fee_amount")
            .HasPrecision(12, 2)
            .IsRequired();
        builder.Property(payout => payout.NetAmount)
            .HasColumnName("net_amount")
            .HasPrecision(12, 2)
            .IsRequired();
        builder.Property(payout => payout.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();
        builder.Property(payout => payout.PolicyVersion)
            .HasColumnName("policy_version")
            .IsRequired();
        builder.Property(payout => payout.ChargeRuleVersion)
            .HasColumnName("charge_rule_version");
        builder.Property(payout => payout.BankCode)
            .HasColumnName("bank_code")
            .HasMaxLength(CaregiverPayoutAccount.MaximumBankCodeLength)
            .IsRequired();
        builder.Property(payout => payout.MaskedIban)
            .HasColumnName("masked_iban")
            .HasMaxLength(8)
            .IsRequired();
        builder.Property(payout => payout.TransferReference)
            .HasColumnName("transfer_reference")
            .HasMaxLength(CaregiverPayout.MaximumReferenceLength)
            .IsRequired();
        builder.Property(payout => payout.Evidence)
            .HasColumnName("evidence")
            .HasMaxLength(CaregiverPayout.MaximumEvidenceLength)
            .IsRequired();
        builder.Property(payout => payout.Reason)
            .HasColumnName("reason")
            .HasMaxLength(CaregiverPayout.MaximumReasonLength)
            .IsRequired();
        builder.Property(payout => payout.Status)
            .HasColumnName("status")
            .IsRequired();
        builder.Property(payout => payout.RecordedBy)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("recorded_by")
            .IsRequired();
        builder.Property(payout => payout.RecordedOnUtc)
            .HasColumnName("recorded_on_utc")
            .IsRequired();
        builder.Property(payout => payout.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(CaregiverPayout.MaximumReasonLength);
        builder.Property(payout => payout.FailedBy)
            .HasConversion(id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null)
            .HasColumnName("failed_by");
        builder.Property(payout => payout.FailedOnUtc)
            .HasColumnName("failed_on_utc");
        builder.Ignore(payout => payout.DomainEvents);
    }
}
