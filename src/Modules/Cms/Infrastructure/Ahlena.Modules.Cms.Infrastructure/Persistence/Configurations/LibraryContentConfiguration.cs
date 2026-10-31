using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Cms.Domain.Library;

namespace Ahlena.Modules.Cms.Infrastructure.Persistence.Configurations;

public sealed class LibraryContentConfiguration : IEntityTypeConfiguration<LibraryContent>
{
    public void Configure(EntityTypeBuilder<LibraryContent> builder)
    {
        builder.ToTable("library_contents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .HasConversion(id => id.Value, value => new LibraryContentId(value));
        builder.Property(x => x.Type)
            .HasColumnName("type")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(x => x.CategoryId)
            .HasColumnName("category_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new LibraryCategoryId(value.Value) : null);
        builder.HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Property(x => x.ArabicTitle)
            .HasColumnName("arabic_title")
            .HasMaxLength(LibraryContent.MaximumTitleLength)
            .IsRequired();
        builder.Property(x => x.EnglishTitle)
            .HasColumnName("english_title")
            .HasMaxLength(LibraryContent.MaximumTitleLength)
            .IsRequired();
        builder.Property(x => x.ArabicSummary)
            .HasColumnName("arabic_summary")
            .HasMaxLength(LibraryContent.MaximumSummaryLength)
            .IsRequired();
        builder.Property(x => x.EnglishSummary)
            .HasColumnName("english_summary")
            .HasMaxLength(LibraryContent.MaximumSummaryLength)
            .IsRequired();
        builder.Property(x => x.VideoUrl)
            .HasColumnName("video_url")
            .HasMaxLength(LibraryContent.MaximumVideoUrlLength);
        builder.Property(x => x.VideoStorageKey)
            .HasColumnName("video_storage_key")
            .HasMaxLength(LibraryContent.MaximumVideoStorageKeyLength);
        builder.Ignore(x => x.HasStoredVideo);
        builder.Property(x => x.DurationSeconds).HasColumnName("duration_seconds");
        builder.Property(x => x.CoverImagePath)
            .HasColumnName("cover_image_path")
            .HasMaxLength(LibraryContent.MaximumCoverPathLength)
            .IsRequired();
        builder.Property(x => x.IsPremium).HasColumnName("is_premium").IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(x => x.ViewCount).HasColumnName("view_count").IsRequired();
        builder.Property(x => x.LikesCount).HasColumnName("likes_count").IsRequired();
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired();
        builder.Property(x => x.PublishedOnUtc).HasColumnName("published_on_utc");
        builder.HasIndex(x => new { x.Status, x.CreatedOnUtc });
        builder.HasIndex(x => new { x.Status, x.ViewCount });
        builder.HasIndex(x => x.CategoryId);
        builder.OwnsMany(x => x.Sections, sections =>
        {
            sections.ToTable("library_content_sections");
            sections.WithOwner().HasForeignKey("LibraryContentId");
            sections.Property<LibraryContentId>("LibraryContentId").HasColumnName("library_content_id");
            sections.HasKey(x => x.Id);
            sections.Property(x => x.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();
            sections.Property(x => x.DisplayOrder)
                .HasColumnName("display_order")
                .IsRequired();
            sections.Property(x => x.ArabicTitle)
                .HasColumnName("arabic_title")
                .HasMaxLength(LibraryContentSection.MaximumTitleLength)
                .IsRequired();
            sections.Property(x => x.EnglishTitle)
                .HasColumnName("english_title")
                .HasMaxLength(LibraryContentSection.MaximumTitleLength)
                .IsRequired();
            sections.Property(x => x.ArabicBullets)
                .HasColumnName("arabic_bullets")
                .HasColumnType("jsonb")
                .IsRequired();
            sections.Property(x => x.EnglishBullets)
                .HasColumnName("english_bullets")
                .HasColumnType("jsonb")
                .IsRequired();
            sections.HasIndex("LibraryContentId", "DisplayOrder").IsUnique();
        });
        builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class LibraryContentInteractionConfiguration
    : IEntityTypeConfiguration<LibraryContentInteraction>
{
    public void Configure(EntityTypeBuilder<LibraryContentInteraction> builder)
    {
        builder.ToTable("library_content_interactions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(x => x.ContentId)
            .HasColumnName("content_id")
            .HasConversion(id => id.Value, value => new LibraryContentId(value));
        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .HasConversion(id => id.Value, value => new UserId(value));
        builder.Property(x => x.Kind)
            .HasColumnName("kind")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(x => x.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();
        builder.HasIndex(x => new { x.ContentId, x.UserId, x.Kind }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.Kind });
    }
}
