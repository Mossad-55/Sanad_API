using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Cms.Domain.Settings;

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Configurations;

public sealed class SiteSettingsConfiguration :
    IEntityTypeConfiguration<SiteSettings>
{
    public void Configure(
        EntityTypeBuilder<SiteSettings> builder)
    {
        // The single-row invariant is enforced in persistence as well as in
        // the domain: the only key the domain can use is 1, and the check
        // constraint makes a second row impossible.
        builder.ToTable(
            "site_settings",
            table => table.HasCheckConstraint(
                "ck_site_settings_single_row",
                "\"id\" = 1"));

        builder.HasKey(settings => settings.Id);

        builder.Property(settings => settings.Id)
            .HasConversion(
                id => id.Value,
                value => new SiteSettingsId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(settings => settings.NavbarLinksJson)
            .HasColumnName("navbar_links_json")
            .HasMaxLength(SiteSettings.MaximumNavbarLinksJsonLength)
            .IsRequired();

        builder.Property(settings => settings.ArabicSeoTitle)
            .HasColumnName("arabic_seo_title")
            .HasMaxLength(SiteSettings.MaximumSeoTitleLength);

        builder.Property(settings => settings.EnglishSeoTitle)
            .HasColumnName("english_seo_title")
            .HasMaxLength(SiteSettings.MaximumSeoTitleLength);

        builder.Property(settings => settings.ArabicSeoDescription)
            .HasColumnName("arabic_seo_description")
            .HasMaxLength(SiteSettings.MaximumSeoDescriptionLength);

        builder.Property(settings => settings.EnglishSeoDescription)
            .HasColumnName("english_seo_description")
            .HasMaxLength(SiteSettings.MaximumSeoDescriptionLength);

        builder.Property(settings => settings.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(settings => settings.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();

        builder.Ignore(settings => settings.DomainEvents);
    }
}
