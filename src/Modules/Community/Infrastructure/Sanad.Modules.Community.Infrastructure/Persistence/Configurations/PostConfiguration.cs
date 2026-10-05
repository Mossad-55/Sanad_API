using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Infrastructure.Persistence.Configurations;

public class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> builder)
    {
        builder.ToTable("Posts", "community");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new CommunityPostId(value));
        builder.Property(e => e.AuthorId)
            .HasConversion(id => id.Value, value => new UserId(value));
        builder.Property(e => e.TitleArabic).HasMaxLength(200);
        builder.Property(e => e.TitleEnglish).HasMaxLength(200);
        builder.Property(e => e.ContentArabic).HasColumnType("text");
        builder.Property(e => e.ContentEnglish).HasColumnType("text");
        builder.Property(e => e.ImageUrl).HasMaxLength(500);
        builder.Property(e => e.Status).HasConversion<int>();
        builder.Property(e => e.CreatedOnUtc)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedOnUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);
        builder.Property(e => e.PublishedOnUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);
    }
}
