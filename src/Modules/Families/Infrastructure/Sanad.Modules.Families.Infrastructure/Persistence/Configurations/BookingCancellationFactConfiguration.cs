using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class BookingCancellationFactConfiguration : IEntityTypeConfiguration<BookingCancellationFact>
{
    public void Configure(EntityTypeBuilder<BookingCancellationFact> builder)
    {
        builder.ToTable("booking_cancellation_facts");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id)
            .HasConversion(id => id.Value, value => new BookingCancellationFactId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(f => f.BookingId)
            .HasConversion(id => id.Value, value => new BookingId(value))
            .HasColumnName("booking_id")
            .IsRequired();

        builder.Property(f => f.ActorSide)
            .HasColumnName("actor_side")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(f => f.ActorUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("actor_user_id")
            .IsRequired();

        builder.Property(f => f.Action)
            .HasColumnName("action")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(f => f.StatusAtCancellation)
            .HasColumnName("status_at_cancellation")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(f => f.CancelledOnUtc)
            .HasColumnName("cancelled_on_utc")
            .IsRequired();

        builder.Property(f => f.ConfirmedOnUtcUsed)
            .HasColumnName("confirmed_on_utc_used");

        builder.Property(f => f.PolicyVersion)
            .HasColumnName("policy_version")
            .IsRequired();

        builder.Property(f => f.ReasonCategory)
            .HasColumnName("reason_category")
            .HasConversion<int>();

        builder.Property(f => f.ReasonNote)
            .HasColumnName("reason_note")
            .HasMaxLength(Booking.MaximumReasonLength);

        builder.Property(f => f.RefundEntitlement)
            .HasColumnName("refund_entitlement")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(f => f.RefundDecisionReason)
            .HasColumnName("refund_decision_reason")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(f => f.IsCaregiverIncident)
            .HasColumnName("is_caregiver_incident")
            .IsRequired();

        // Exactly one durable cancellation fact per booking, enforced at the database level.
        builder.HasIndex(f => f.BookingId)
            .IsUnique()
            .HasDatabaseName("ux_booking_cancellation_facts_booking");

        builder.HasOne<Booking>()
            .WithMany()
            .HasForeignKey(f => f.BookingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
