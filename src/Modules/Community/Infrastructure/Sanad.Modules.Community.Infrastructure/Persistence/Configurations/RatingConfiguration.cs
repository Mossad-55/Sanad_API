using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Ratings;

namespace Sanad.Modules.Community.Infrastructure.Persistence.Configurations;

public sealed class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> builder)
    {
        builder.ToTable("Ratings", CommunityDbContext.Schema);
        builder.HasKey(rating => rating.Id);
        builder.Property(rating => rating.Id)
            .HasConversion(id => id.Value, value => new CommunityRatingId(value));
        builder.Property(rating => rating.PostId)
            .HasConversion(id => id.Value, value => new CommunityPostId(value));
        builder.Property(rating => rating.UserId)
            .HasConversion(id => id.Value, value => new UserId(value));
        builder.Property(rating => rating.RatingValue).HasMaxLength(5);
        builder.Property(rating => rating.CreatedOnUtc)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(rating => rating.Post)
            .WithMany()
            .HasForeignKey(rating => rating.PostId);
        builder.HasIndex(rating => new { rating.PostId, rating.UserId }).IsUnique();
    }
}
