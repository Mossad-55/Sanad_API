using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Cms.Domain.Landing;

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Configurations;

public sealed class LandingSectionConfiguration :
    IEntityTypeConfiguration<LandingSection>
{
    public void Configure(EntityTypeBuilder<LandingSection> builder)
    {
        builder.ToTable("landing_sections");

        builder.HasKey(section => section.Id);

        builder.Property(section => section.Id)
            .HasConversion(
                id => id.Value,
                value => new LandingSectionId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(section => section.Type)
            .HasColumnName("type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(section => section.ArabicTitle)
            .HasColumnName("arabic_title")
            .HasMaxLength(LandingSection.MaximumTitleLength)
            .IsRequired();

        builder.Property(section => section.EnglishTitle)
            .HasColumnName("english_title")
            .HasMaxLength(LandingSection.MaximumTitleLength)
            .IsRequired();

        builder.Property(section => section.ArabicBody)
            .HasColumnName("arabic_body")
            .HasMaxLength(LandingSection.MaximumBodyLength);

        builder.Property(section => section.EnglishBody)
            .HasColumnName("english_body")
            .HasMaxLength(LandingSection.MaximumBodyLength);

        builder.Property(section => section.ContentJson)
            .HasColumnName("content_json")
            .HasMaxLength(LandingSection.MaximumContentJsonLength);

        builder.Property(section => section.ImagePath)
            .HasColumnName("image_path")
            .HasMaxLength(LandingSection.MaximumImagePathLength);

        builder.Property(section => section.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.Property(section => section.IsVisible)
            .HasColumnName("is_visible")
            .IsRequired();

        builder.Property(section => section.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(section => section.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(section => section.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();

        builder.Property(section => section.PublishedOnUtc)
            .HasColumnName("published_on_utc");

        builder.HasIndex(section => new
        {
            section.Status,
            section.DisplayOrder
        });

        builder.Ignore(section => section.DomainEvents);
    }
}
