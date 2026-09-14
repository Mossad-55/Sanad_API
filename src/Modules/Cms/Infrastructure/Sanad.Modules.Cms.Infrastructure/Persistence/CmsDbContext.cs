using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Help;
using Sanad.Modules.Cms.Domain.Legal;
using Sanad.Modules.Cms.Domain.Splash;

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
}
