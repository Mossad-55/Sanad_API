using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Bookings;

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Configurations;

public sealed class CareHomeInternalBookingNoteConfiguration : IEntityTypeConfiguration<CareHomeInternalBookingNote>
{
    public void Configure(EntityTypeBuilder<CareHomeInternalBookingNote> b)
    {
        b.ToTable("internal_booking_notes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        b.Property(x => x.Author).HasColumnName("author_user_id")
            .HasConversion(x => x.Value, x => new UserId(x)).IsRequired();
        b.Property(x => x.Text).HasColumnName("text").HasMaxLength(4000).IsRequired();
        b.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        b.HasIndex(x => new { x.BookingId, x.CreatedOnUtc });
        b.HasOne<CareHomeBooking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
    }
}
