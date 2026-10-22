using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Rentals.Domain;

namespace Sanad.Modules.Rentals.Infrastructure.Persistence.Configurations;

/// <summary>
/// One gateway payment attempt for a booking. The <c>paymob_order_id</c>
/// unique index is the intent idempotency key — the webhook's
/// <c>merchant_order_id</c> lookup rides on it (the recorded order id carries
/// the <c>rnt_</c> prefix and is owned by this module). The captured amount
/// column is the module-standard <c>numeric(18,2)</c> (Owner decision #37).
/// </summary>
public sealed class RentalPaymentTransactionConfiguration : IEntityTypeConfiguration<RentalPaymentTransaction>
{
    public void Configure(EntityTypeBuilder<RentalPaymentTransaction> builder)
    {
        builder.ToTable("rental_payment_transactions", table =>
        {
            table.HasCheckConstraint("ck_rental_payment_transactions_method", "method IN (1, 2)");
            table.HasCheckConstraint("ck_rental_payment_transactions_status", "status IN (1, 2, 3)");
            table.HasCheckConstraint("ck_rental_payment_transactions_amount_positive", "amount > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.RentalBookingId).HasColumnName("rental_booking_id").IsRequired();
        builder.Property(x => x.PaymobOrderId).HasColumnName("paymob_order_id").HasMaxLength(RentalPaymentTransaction.MaximumPaymobOrderIdLength).IsRequired();
        builder.Property(x => x.PaymobTransactionId).HasColumnName("paymob_transaction_id").HasMaxLength(RentalPaymentTransaction.MaximumPaymobTransactionIdLength);
        builder.Property(x => x.Method).HasColumnName("method").HasConversion<int>().IsRequired();
        RentalMoneyColumns.UseMoneyMajorUnits(builder.Property(x => x.AmountMinorUnits), "amount");
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(x => x.SettledOnUtc).HasColumnName("settled_on_utc");
        builder.Property(x => x.FailedOnUtc).HasColumnName("failed_on_utc");
        builder.HasIndex(x => x.PaymobOrderId).IsUnique().HasDatabaseName("ux_rental_payment_transactions_paymob_order_id");
        builder.HasIndex(x => x.RentalBookingId).HasDatabaseName("ix_rental_payment_transactions_rental_booking_id");
    }
}
