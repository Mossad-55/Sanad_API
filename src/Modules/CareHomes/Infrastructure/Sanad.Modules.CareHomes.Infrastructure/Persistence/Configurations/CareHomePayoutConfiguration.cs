using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Configurations;

public sealed class CareHomePayoutConfiguration : IEntityTypeConfiguration<CareHomePayout>
{
    public void Configure(EntityTypeBuilder<CareHomePayout> b)
    {
        b.ToTable("payouts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        b.Property(x => x.FacilityId).HasColumnName("facility_id")
            .HasConversion(x => x.Value, x => new CareHomeId(x)).IsRequired();
        b.Property(x => x.GrossAmount).HasColumnName("gross_amount").HasPrecision(18, 2).IsRequired();
        b.Property(x => x.RemainingCustomerAmount).HasColumnName("remaining_customer_amount").HasPrecision(18, 2).IsRequired();
        b.Property(x => x.FeeRatePercentage).HasColumnName("fee_rate_percentage").HasPrecision(9, 4).IsRequired();
        b.Property(x => x.FeeAmount).HasColumnName("fee_amount").HasPrecision(18, 2).IsRequired();
        b.Property(x => x.NetAmount).HasColumnName("net_amount").HasPrecision(18, 2).IsRequired();
        b.Property(x => x.ChargeRuleVersion).HasColumnName("charge_rule_version").IsRequired();
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(x => x.TransferReference).HasColumnName("transfer_reference").HasMaxLength(200).IsRequired();
        b.Property(x => x.Evidence).HasColumnName("evidence").HasMaxLength(2000).IsRequired();
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(2000).IsRequired();
        b.Property(x => x.RecordedBy).HasColumnName("recorded_by")
            .HasConversion(x => x.Value, x => new UserId(x)).IsRequired();
        b.Property(x => x.RecordedOnUtc).HasColumnName("recorded_on_utc").IsRequired();
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
        b.HasIndex(x => x.BookingId).IsUnique();
        b.HasIndex(x => new { x.FacilityId, x.RecordedOnUtc });
        b.HasOne<CareHomeBooking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CareHomeFacility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CareHomePayoutDebtConfiguration : IEntityTypeConfiguration<CareHomePayoutDebt>
{
    public void Configure(EntityTypeBuilder<CareHomePayoutDebt> b)
    {
        b.ToTable("payout_debts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.PayoutId).HasColumnName("payout_id").IsRequired();
        b.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        b.Property(x => x.CustomerRefundAmount).HasColumnName("customer_refund_amount").HasPrecision(18, 2).IsRequired();
        b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 2).IsRequired();
        b.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(200).IsRequired();
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(2000).IsRequired();
        b.Property(x => x.RecordedBy).HasColumnName("recorded_by")
            .HasConversion(x => x.Value, x => new UserId(x)).IsRequired();
        b.Property(x => x.RecordedOnUtc).HasColumnName("recorded_on_utc").IsRequired();

        b.HasIndex(x => new { x.PayoutId, x.Reference }).IsUnique();
        b.HasOne<CareHomePayout>().WithMany().HasForeignKey(x => x.PayoutId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CareHomeBooking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
    }
}