using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.Modules.Community.Domain.Categories;
using Ahlena.Modules.Community.Domain.Posts;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Community.Infrastructure.Persistence.Configurations;

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
        builder.Property(e => e.CategoryId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CommunityCategoryId(value.Value) : null);
        builder.HasOne(e => e.Category)
            .WithMany()
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Property(e => e.TitleArabic).HasMaxLength(200);
        builder.Property(e => e.TitleEnglish).HasMaxLength(200);
        builder.Property(e => e.ContentArabic).HasColumnType("text");
        builder.Property(e => e.ContentEnglish).HasColumnType("text");
        builder.Property(e => e.ImageUrl).HasMaxLength(500);
        builder.Property(e => e.Status).HasConversion<int>();
        // The lifecycle-state concurrency token (module convention: the
        // CommunityPostReport Version token). A stale author edit or deletion
        // that races a concurrent transition fails its save instead of
        // resurrecting the post or losing the deletion.
        builder.Property(e => e.Version)
            .IsConcurrencyToken()
            .IsRequired();
        builder.Property(e => e.DeletedBy)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : (UserId?)null)
            .IsRequired(false);
        builder.Property(e => e.DeletedOnUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);
        builder.Property(e => e.CreatedOnUtc)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedOnUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);
        builder.Property(e => e.PublishedOnUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);
        builder.Property(e => e.RejectedBy)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : (UserId?)null)
            .IsRequired(false);
        builder.Property(e => e.RejectionReasonCode)
            .HasMaxLength(CommunityRejectionReasonCodes.MaximumLength)
            .IsRequired(false);
        builder.Property(e => e.RejectionNote)
            .HasMaxLength(Post.MaximumNoteLength)
            .IsRequired(false);
        builder.Property(e => e.RejectedOnUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);
    }
}
