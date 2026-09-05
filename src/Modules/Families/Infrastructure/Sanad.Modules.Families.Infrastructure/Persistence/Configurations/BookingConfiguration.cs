using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(id => id.Value, value => new BookingId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(b => b.FamilyId)
            .HasConversion(id => id.Value, value => new FamilyId(value))
            .HasColumnName("family_id")
            .IsRequired();

        builder.Property(b => b.CreatedByUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(b => b.ElderlyId)
            .HasConversion(id => id.Value, value => new ElderlyId(value))
            .HasColumnName("elderly_id")
            .IsRequired();

        builder.Property(b => b.CaregiverId)
            .HasConversion(id => id.Value, value => new CaregiverId(value))
            .HasColumnName("caregiver_id")
            .IsRequired();

        builder.Property(b => b.CaregiverType)
            .HasColumnName("caregiver_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(b => b.ShiftType)
            .HasColumnName("shift_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(b => b.BookingDate)
            .HasColumnName("booking_date")
            .IsRequired();

        builder.Property(b => b.StartTime)
            .HasColumnName("start_time")
            .IsRequired();

        builder.Property(b => b.EndTime)
            .HasColumnName("end_time")
            .IsRequired();

        builder.Property(b => b.AcceptanceDeadlineUtc)
            .HasColumnName("acceptance_deadline_utc")
            .IsRequired();

        builder.Property(b => b.ExpiredOnUtc)
            .HasColumnName("expired_on_utc");

        builder.Property(b => b.ServiceAddress)
            .HasColumnName("service_address")
            .HasMaxLength(Booking.MaximumAddressLength)
            .IsRequired();

        builder.Property(b => b.SpecialInstructions)
            .HasColumnName("special_instructions")
            .HasMaxLength(Booking.MaximumInstructionsLength);

        builder.Property(b => b.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(b => b.PaymobOrderId)
            .HasColumnName("paymob_order_id")
            .HasMaxLength(100);

        builder.Property(b => b.PaymobTransactionId)
            .HasColumnName("paymob_transaction_id")
            .HasMaxLength(100);

        builder.Property(b => b.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(Booking.MaximumReasonLength);

        builder.Property(b => b.CaregiverNotes)
            .HasColumnName("caregiver_notes")
            .HasMaxLength(Booking.MaximumNotesLength);

        builder.Property(b => b.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(b => b.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired();
        builder.Property(b => b.PaidOnUtc).HasColumnName("paid_on_utc");
        builder.Property(b => b.ConfirmedOnUtc).HasColumnName("confirmed_on_utc");
        builder.Property(b => b.StartedOnUtc).HasColumnName("started_on_utc");
        builder.Property(b => b.CompletedOnUtc).HasColumnName("completed_on_utc");
        builder.Property(b => b.CancelledOnUtc).HasColumnName("cancelled_on_utc");

        builder.OwnsOne(b => b.PriceSnapshot, snapshot =>
        {
            snapshot.Property(p => p.BaseCaregiverFee)
                .HasColumnName("price_base_fee")
                .HasPrecision(12, 2)
                .IsRequired();
            snapshot.Property(p => p.PlatformFeePercentage)
                .HasColumnName("price_platform_fee_percentage")
                .HasPrecision(5, 2)
                .IsRequired();
            snapshot.Property(p => p.PlatformFeeAmount)
                .HasColumnName("price_platform_fee_amount")
                .HasPrecision(12, 2)
                .IsRequired();
            snapshot.Property(p => p.TotalPayableAmount)
                .HasColumnName("price_total_payable_amount")
                .HasPrecision(12, 2)
                .IsRequired();
            snapshot.Property(p => p.Currency)
                .HasColumnName("price_currency")
                .HasMaxLength(10)
                .IsRequired();
        });

        builder.HasIndex(b => b.FamilyId);
        builder.HasIndex(b => b.CaregiverId);
        builder.HasIndex(b => b.ElderlyId);
        builder.HasIndex(b => b.Status);
    }
}