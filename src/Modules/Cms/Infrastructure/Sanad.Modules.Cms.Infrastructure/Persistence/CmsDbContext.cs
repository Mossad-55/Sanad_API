using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Help;
using Sanad.Modules.Cms.Domain.Legal;
using Sanad.Modules.Cms.Domain.Splash;
using Sanad.Modules.Cms.Domain.Wellness;
using Sanad.Modules.Cms.Domain.SentenceBuilder;

namespace Sanad.Modules.Cms.Infrastructure.Persistence;

public sealed class CmsDbContext :
    DbContext,
    ICmsDbContext
{
    public const string Schema = "cms";

    public CmsDbContext(
        DbContextOptions<CmsDbContext> options)
        : base(options)
    {
    }

    public DbSet<SplashScreen> SplashScreens =>
        Set<SplashScreen>();

    public DbSet<LegalDocument> LegalDocuments =>
        Set<LegalDocument>();

    public DbSet<HelpFaq> HelpFaqs =>
        Set<HelpFaq>();

    public DbSet<SupportContact> SupportContacts =>
        Set<SupportContact>();

    public DbSet<WellnessTip> WellnessTips => Set<WellnessTip>();
    public DbSet<SentenceBuilderCatalogEntry> SentenceBuilderCatalogEntries => Set<SentenceBuilderCatalogEntry>();
    public DbSet<SentenceBuilderCatalogRevision> SentenceBuilderCatalogRevisions => Set<SentenceBuilderCatalogRevision>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(
            Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CmsDbContext).Assembly);

        base.OnModelCreating(
            modelBuilder);
    }

    public override int SaveChanges()
        => SaveChanges(acceptAllChangesOnSuccess: true);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ThrowIfSentenceBuilderRevisionMutated();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ThrowIfSentenceBuilderRevisionMutated();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ThrowIfSentenceBuilderRevisionMutated()
    {
        foreach (var entry in ChangeTracker.Entries<SentenceBuilderCatalogRevision>())
        {
            if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Sentence-builder catalog revisions are immutable and cannot be deleted.");

            if (entry.State == EntityState.Modified && entry.Properties.Any(property =>
                    property.IsModified && property.Metadata.Name is not nameof(SentenceBuilderCatalogRevision.IsActive)))
                throw new InvalidOperationException("Sentence-builder catalog fields are immutable; only activation may change.");
        }
    }
}
