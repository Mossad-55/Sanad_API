using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Configurations;

/// <summary>
/// F7 append-only Care Home-rating moderation audit. The table is insert-only:
/// no update or delete path exists, so the full hide/restore history is
/// retained even after a restore clears the rating's current hide fields.
/// </summary>
public sealed class CareHomeRatingModerationActionConfiguration
    : IEntityTypeConfiguration<CareHomeRatingModerationAction>
{
    public void Configure(EntityTypeBuilder<CareHomeRatingModerationAction> b)
    {
        b.ToTable("rating_moderation_actions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.RatingId).HasColumnName("rating_id").IsRequired();
        b.Property(x => x.CareHomeId)
            .HasConversion(x => x.Value, x => new CareHomeId(x))
            .HasColumnName("care_home_id").IsRequired();
        b.Property(x => x.Action).HasConversion<int>().HasColumnName("action").IsRequired();
        b.Property(x => x.ActorUserId)
            .HasConversion(x => x.Value, x => new UserId(x))
            .HasColumnName("actor_user_id").IsRequired();
        b.Property(x => x.Reason).HasColumnName("reason")
            .HasMaxLength(CareHomeRating.MaximumModerationReasonLength).IsRequired();
        b.Property(x => x.OccurredOnUtc).HasColumnName("occurred_on_utc").IsRequired();

        b.HasOne<CareHomeRating>().WithMany().HasForeignKey(x => x.RatingId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.RatingId, x.OccurredOnUtc });
    }
}
