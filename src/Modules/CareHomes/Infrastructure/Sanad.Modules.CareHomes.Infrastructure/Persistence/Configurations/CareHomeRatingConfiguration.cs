using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Configurations;

public sealed class CareHomeRatingConfiguration : IEntityTypeConfiguration<CareHomeRating>
{
    public void Configure(EntityTypeBuilder<CareHomeRating> b)
    {
        b.ToTable("ratings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        b.Property(x => x.CareHomeId).HasConversion(x => x.Value, x => new CareHomeId(x)).HasColumnName("care_home_id").IsRequired();
        b.Property(x => x.FamilyId).HasConversion(x => x.Value, x => new FamilyId(x)).HasColumnName("family_id").IsRequired();
        b.Property(x => x.CreatedByUserId).HasConversion(x => x.Value, x => new UserId(x)).HasColumnName("created_by_user_id").IsRequired();
        b.Property(x => x.UpdatedByUserId).HasConversion(x => x.Value, x => new UserId(x)).HasColumnName("updated_by_user_id").IsRequired();
        b.Property(x => x.Stars).HasColumnName("stars").IsRequired();
        b.Property(x => x.ReviewText).HasColumnName("review_text").HasMaxLength(CareHomeRating.MaximumReviewTextLength);
        b.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        b.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired();
        b.HasOne<CareHomeFacility>().WithMany().HasForeignKey(x => x.CareHomeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CareHomeBooking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.BookingId).IsUnique();
        b.HasIndex(x => new { x.CareHomeId, x.Stars });
    }
}
