using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Infrastructure.Persistence.Configurations;

public class ReplyConfiguration : IEntityTypeConfiguration<Reply>
{
    public void Configure(EntityTypeBuilder<Reply> builder)
    {
        builder.ToTable("Replies", "community");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new CommunityReplyId(value));
        builder.Property(e => e.CommentId)
            .HasConversion(id => id.Value, value => new CommunityCommentId(value));
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
        builder.HasOne<Comment>().WithMany(e => e.Replies).HasForeignKey(e => e.CommentId);
    }
}
