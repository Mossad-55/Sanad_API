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

        builder.Property(rating => rating.ModerationState)
            .HasConversion<int>()
            .HasColumnName("moderation_state")
            .IsRequired();
        builder.Property(rating => rating.HiddenByUserId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : (UserId?)null)
            .HasColumnName("hidden_by_user_id");
        builder.Property(rating => rating.HiddenOnUtc)
            .HasColumnName("hidden_on_utc");
        builder.Property(rating => rating.HideReason)
            .HasColumnName("hide_reason")
            .HasMaxLength(CaregiverRating.MaximumModerationReasonLength);

        builder.HasOne<Caregiver>()
            .WithMany()
            .HasForeignKey(rating => rating.CaregiverId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(rating => rating.BookingId)
            .IsUnique();
        builder.HasIndex(rating => new { rating.CaregiverId, rating.Stars });
        // Moderation reads and aggregate recomputes filter on the state.
        builder.HasIndex(rating => new { rating.CaregiverId, rating.ModerationState });
    }
}
