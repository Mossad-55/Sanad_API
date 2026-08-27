using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Cms.Infrastructure.Persistence;

namespace Sanad.UnitTests.Cms;

public sealed class CmsDbContextModelTests
{
    [Fact]
    public void Model_ShouldUseCmsSchema()
    {
        using CmsDbContext dbContext =
            CreateDbContext();

        Assert.Equal(
            CmsDbContext.Schema,
            dbContext.Model.GetDefaultSchema());
    }

    [Fact]
    public void Model_ShouldMapSplashScreensTable()
    {
        using CmsDbContext dbContext =
            CreateDbContext();

        bool tableExists =
            dbContext.Model
                .GetEntityTypes()
                .Any(entityType =>
                    entityType.GetTableName() ==
                    "splash_screens");

        Assert.True(
            tableExists);
    }

    [Fact]
    public void Model_ShouldMapUniqueInternalName()
    {
        using CmsDbContext dbContext =
            CreateDbContext();

        var entityType =
            dbContext.Model.FindEntityType(
                typeof(Sanad.Modules.Cms.Domain.Splash.SplashScreen));

        Assert.NotNull(entityType);

        Assert.Contains(
            entityType!.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties.Count == 1 &&
                index.Properties[0].Name ==
                    "InternalName");
    }

    private static CmsDbContext CreateDbContext()
    {
        DbContextOptions<CmsDbContext> options =
            new DbContextOptionsBuilder<
                CmsDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString())
                .Options;

        return new CmsDbContext(
            options);
    }
}