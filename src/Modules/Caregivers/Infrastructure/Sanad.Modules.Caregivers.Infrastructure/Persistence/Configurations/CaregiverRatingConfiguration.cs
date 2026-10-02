using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Configurations;

public sealed class CaregiverRatingConfiguration : IEntityTypeConfiguration<CaregiverRating>
{
    public void Configure(EntityTypeBuilder<CaregiverRating> builder)
    {
        builder.ToTable("caregiver_ratings");

        builder.HasKey(rating => rating.Id);
        builder.Property(rating => rating.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(rating => rating.CaregiverId)
            .HasConversion(id => id.Value, value => new CaregiverId(value))
            .HasColumnName("caregiver_id")
            .IsRequired();
        builder.Property(rating => rating.BookingId)
            .HasColumnName("booking_id")
            .IsRequired();
        builder.Property(rating => rating.FamilyId)
            .HasConversion(id => id.Value, value => new FamilyId(value))
            .HasColumnName("family_id")
            .IsRequired();
        builder.Property(rating => rating.CreatedByUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("created_by_user_id")
            .IsRequired();
        builder.Property(rating => rating.UpdatedByUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("updated_by_user_id")
            .IsRequired();
        builder.Property(rating => rating.Stars)
            .HasColumnName("stars")
            .IsRequired();
        builder.Property(rating => rating.ReviewText)
            .HasColumnName("review_text")
            .HasMaxLength(CaregiverRating.MaximumReviewTextLength);
        builder.Property(rating => rating.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();
        builder.Property(rating => rating.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();

        builder.HasOne<Caregiver>()
            .WithMany()
            .HasForeignKey(rating => rating.CaregiverId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(rating => rating.BookingId)
            .IsUnique();
        builder.HasIndex(rating => new { rating.CaregiverId, rating.Stars });
    }
}
