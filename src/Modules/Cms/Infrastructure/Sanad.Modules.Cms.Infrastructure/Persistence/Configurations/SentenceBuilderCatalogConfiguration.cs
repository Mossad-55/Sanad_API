using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Cms.Domain.SentenceBuilder;

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Configurations;
public sealed class SentenceBuilderCatalogEntryConfiguration : IEntityTypeConfiguration<SentenceBuilderCatalogEntry>
{
    public void Configure(EntityTypeBuilder<SentenceBuilderCatalogEntry> b)
    { b.ToTable("sentence_builder_catalog_entries"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.StableKey).HasMaxLength(120).IsRequired(); b.Property(x => x.Category).HasConversion<int>().IsRequired(); b.Property(x => x.CreatedOnUtc).IsRequired(); b.HasIndex(x => new { x.StableKey, x.Category }).IsUnique(); b.HasMany(x => x.Revisions).WithOne(x => x.CatalogEntry).HasForeignKey(x => x.CatalogEntryId).OnDelete(DeleteBehavior.Cascade); }
}
public sealed class SentenceBuilderCatalogRevisionConfiguration : IEntityTypeConfiguration<SentenceBuilderCatalogRevision>
{
    public void Configure(EntityTypeBuilder<SentenceBuilderCatalogRevision> b)
    { b.ToTable("sentence_builder_catalog_revisions"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.ArabicLabel).HasMaxLength(200).IsRequired(); b.Property(x => x.EnglishLabel).HasMaxLength(200).IsRequired(); b.Property(x => x.CreatedOnUtc).IsRequired(); b.HasIndex(x => new { x.CatalogEntryId, x.Version }).IsUnique(); b.HasIndex(x => new { x.CatalogEntryId, x.IsActive }).IsUnique().HasFilter("\"IsActive\" = TRUE"); }
}
