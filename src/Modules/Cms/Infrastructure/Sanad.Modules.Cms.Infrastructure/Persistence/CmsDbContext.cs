using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Cms.Application.Abstractions.Data;
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