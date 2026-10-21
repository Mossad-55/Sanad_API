using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Cms.Domain.Landing;

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Configurations;

public sealed class FamilyHomeSectionConfiguration :
    IEntityTypeConfiguration<FamilyHomeSection>
{
    public void Configure(EntityTypeBuilder<FamilyHomeSection> builder)
    {
        builder.ToTable("family_home_sections");

        builder.HasKey(section => section.Id);

        builder.Property(section => section.Id)
            .HasConversion(
                id => id.Value,
                value => new FamilyHomeSectionId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(section => section.Kind)
            .HasColumnName("kind")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(section => section.ItemsJson)
            .HasColumnName("items_json")
            .HasMaxLength(FamilyHomeSection.MaximumItemsJsonLength);

        builder.Property(section => section.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.Property(section => section.IsVisible)
            .HasColumnName("is_visible")
            .IsRequired();

        builder.Property(section => section.SourceId)
            .HasColumnName("source_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new FamilyHomeSectionId(value.Value) : null);

        builder.Property(section => section.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(section => section.Version)
            .HasColumnName("version")
            .IsRequired()
            .IsConcurrencyToken();

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