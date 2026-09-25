using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Cms.Domain.Wellness;

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Configurations;

public sealed class WellnessTipConfiguration : IEntityTypeConfiguration<WellnessTip>
{
    public void Configure(EntityTypeBuilder<WellnessTip> builder)
    {
        builder.ToTable("wellness_tips"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ArabicTitle).HasColumnName("arabic_title").HasMaxLength(WellnessTip.MaximumTitleLength).IsRequired();
        builder.Property(x => x.EnglishTitle).HasColumnName("english_title").HasMaxLength(WellnessTip.MaximumTitleLength).IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(WellnessTip.MaximumCategoryLength).IsRequired();
        builder.Property(x => x.ImagePath).HasColumnName("image_path").HasMaxLength(WellnessTip.MaximumImagePathLength).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired(); builder.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired(); builder.Property(x => x.PublishedOnUtc).HasColumnName("published_on_utc");
        builder.HasIndex(x => new { x.Status, x.Category, x.UpdatedOnUtc });
        builder.OwnsMany(x => x.Sections, s => { s.ToTable("wellness_tip_sections"); s.WithOwner().HasForeignKey("WellnessTipId"); s.Property<Guid>("WellnessTipId").HasColumnName("wellness_tip_id"); s.HasKey(x => x.Id); s.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever(); s.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired(); s.Property(x => x.ArabicText).HasColumnName("arabic_text").HasMaxLength(WellnessTipSection.MaximumTextLength).IsRequired(); s.Property(x => x.EnglishText).HasColumnName("english_text").HasMaxLength(WellnessTipSection.MaximumTextLength).IsRequired(); s.HasIndex("WellnessTipId", "DisplayOrder").IsUnique(); });
        builder.Ignore(x => x.DomainEvents);
    }
}
