using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Cms.Domain.Welcome;

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Configurations;

public sealed class ElderlyWelcomeConfiguration : IEntityTypeConfiguration<ElderlyWelcome>
{
    public void Configure(EntityTypeBuilder<ElderlyWelcome> b)
    {
        b.ToTable("elderly_welcomes"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.ArabicHeadline).HasColumnName("arabic_headline").HasMaxLength(ElderlyWelcome.MaximumTextLength).IsRequired();
        b.Property(x => x.EnglishHeadline).HasColumnName("english_headline").HasMaxLength(ElderlyWelcome.MaximumTextLength).IsRequired();
        b.Property(x => x.ArabicCtaLabel).HasColumnName("arabic_cta_label").HasMaxLength(ElderlyWelcome.MaximumTextLength).IsRequired();
        b.Property(x => x.EnglishCtaLabel).HasColumnName("english_cta_label").HasMaxLength(ElderlyWelcome.MaximumTextLength).IsRequired();
        b.Property(x => x.CtaAction).HasColumnName("cta_action").HasMaxLength(100).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        b.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired(); b.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired(); b.Property(x => x.PublishedOnUtc).HasColumnName("published_on_utc");
        b.HasIndex(x => x.Status);
        b.OwnsMany(x => x.Benefits, t => { t.ToTable("elderly_welcome_benefits"); t.WithOwner().HasForeignKey("ElderlyWelcomeId"); t.Property<Guid>("ElderlyWelcomeId").HasColumnName("elderly_welcome_id"); t.HasKey(x => x.Id); t.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever(); t.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired(); t.Property(x => x.ArabicTitle).HasColumnName("arabic_title").HasMaxLength(ElderlyWelcome.MaximumTextLength).IsRequired(); t.Property(x => x.EnglishTitle).HasColumnName("english_title").HasMaxLength(ElderlyWelcome.MaximumTextLength).IsRequired(); t.Property(x => x.ArabicDescription).HasColumnName("arabic_description").HasMaxLength(ElderlyWelcome.MaximumTextLength).IsRequired(); t.Property(x => x.EnglishDescription).HasColumnName("english_description").HasMaxLength(ElderlyWelcome.MaximumTextLength).IsRequired(); t.HasIndex("ElderlyWelcomeId", "DisplayOrder").IsUnique(); });
        b.Ignore(x => x.DomainEvents);
    }
}
