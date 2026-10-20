using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Configurations;

/// <summary>
/// F7 append-only caregiver-rating moderation audit. The table is insert-only:
/// no update or delete path exists, so the full hide/restore history is
/// retained even after a restore clears the rating's current hide fields.
/// </summary>
public sealed class CaregiverRatingModerationActionConfiguration
    : IEntityTypeConfiguration<CaregiverRatingModerationAction>
{
    public void Configure(EntityTypeBuilder<CaregiverRatingModerationAction> builder)
    {
        builder.ToTable("caregiver_rating_moderation_actions");

        builder.HasKey(action => action.Id);
        builder.Property(action => action.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(action => action.RatingId)
            .HasColumnName("rating_id")
            .IsRequired();
        builder.Property(action => action.CaregiverId)
            .HasConversion(id => id.Value, value => new CaregiverId(value))
            .HasColumnName("caregiver_id")
            .IsRequired();
        builder.Property(action => action.Action)
            .HasConversion<int>()
            .HasColumnName("action")
            .IsRequired();
        builder.Property(action => action.ActorUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("actor_user_id")
            .IsRequired();
        builder.Property(action => action.Reason)
            .HasColumnName("reason")
            .HasMaxLength(CaregiverRating.MaximumModerationReasonLength)
            .IsRequired();
        builder.Property(action => action.OccurredOnUtc)
            .HasColumnName("occurred_on_utc")
            .IsRequired();

        builder.HasOne<CaregiverRating>()
            .WithMany()
            .HasForeignKey(action => action.RatingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(action => new { action.RatingId, action.OccurredOnUtc });
    }
}
