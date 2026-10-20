using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Configurations;

/// <summary>
/// F7 owner rating flags. The filtered unique index is the final arbiter of
/// "at most one unresolved flag per (rating, owner)": a repeat flag is a no-op
/// while a flag is open, and resolution frees the slot for a later flag.
/// </summary>
public sealed class CareHomeRatingFlagConfiguration : IEntityTypeConfiguration<CareHomeRatingFlag>
{
    public void Configure(EntityTypeBuilder<CareHomeRatingFlag> b)
    {
        b.ToTable("rating_flags");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.RatingId).HasColumnName("rating_id").IsRequired();
        b.Property(x => x.CareHomeId)
            .HasConversion(x => x.Value, x => new CareHomeId(x))
            .HasColumnName("care_home_id").IsRequired();
        b.Property(x => x.OwnerUserId)
            .HasConversion(x => x.Value, x => new UserId(x))
            .HasColumnName("owner_user_id").IsRequired();
        b.Property(x => x.Status).HasConversion<int>().HasColumnName("status").IsRequired();
        b.Property(x => x.Reason).HasColumnName("reason")
            .HasMaxLength(CareHomeRatingFlag.MaximumReasonLength).IsRequired();
        b.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        b.Property(x => x.ResolvedOnUtc).HasColumnName("resolved_on_utc");
        b.Property(x => x.ResolvedByUserId)
            .HasConversion(x => x.HasValue ? x.Value.Value : (Guid?)null,
                x => x.HasValue ? new UserId(x.Value) : (UserId?)null)
            .HasColumnName("resolved_by_user_id");
        b.Property(x => x.Resolution).HasConversion<int>().HasColumnName("resolution");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        b.HasOne<CareHomeRating>().WithMany().HasForeignKey(x => x.RatingId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.RatingId, x.OwnerUserId }).IsUnique().HasFilter("\"status\" = 0");
        b.HasIndex(x => x.Status);
    }
}
