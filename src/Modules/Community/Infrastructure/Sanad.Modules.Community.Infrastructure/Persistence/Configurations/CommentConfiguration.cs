using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Infrastructure.Persistence.Configurations;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("Comments", "community");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new CommunityCommentId(value));
        builder.Property(e => e.PostId)
            .HasConversion(id => id.Value, value => new CommunityPostId(value));
        builder.Property(e => e.AuthorId)
            .HasConversion(id => id.Value, value => new UserId(value));
        builder.Property(e => e.ContentArabic).HasMaxLength(500);
        builder.Property(e => e.ContentEnglish).HasMaxLength(500);
        builder.Property(e => e.CreatedOnUtc)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedOnUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);
        builder.HasOne(e => e.Post).WithMany().HasForeignKey(e => e.PostId);
    }
}
