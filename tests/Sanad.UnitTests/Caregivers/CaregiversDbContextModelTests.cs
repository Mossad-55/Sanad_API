using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiversDbContextModelTests
{
    [Fact]
    public void Model_ShouldUseCaregiversSchema()
    {
        using CaregiversDbContext dbContext = CreateDbContext();
        Assert.Equal(CaregiversDbContext.Schema, dbContext.Model.GetDefaultSchema());
    }

    [Fact]
    public void Model_ShouldMapLookupTables()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        string[] expected = ["services", "languages", "governorates",
            "cities", "areas", "specializations", "professional_titles", "academic_degrees"];

        string[] mapped = dbContext.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .Where(n => n is not null)
            .Cast<string>()
            .ToArray();

        foreach (string table in expected)
        {
            Assert.Contains(table, mapped);
        }
    }

    [Fact]
    public void Model_ShouldMapUniqueLanguageCode()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var entityType = dbContext.Model.FindEntityType(
            typeof(Language));

        Assert.NotNull(entityType);
        Assert.Contains(entityType!.GetIndexes(),
            index => index.IsUnique && index.Properties.Count == 1
                && index.Properties[0].Name == "Code");
    }

    [Fact]
    public void Model_ShouldMapCityGovernorateForeignKey()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var property = dbContext.Model.FindEntityType(
            typeof(City))
            ?.FindProperty("GovernorateId");

        Assert.NotNull(property);
        Assert.Equal("governorate_id", property!.GetColumnName());
    }

    private static CaregiversDbContext CreateDbContext()
    {
        DbContextOptions<CaregiversDbContext> options =
            new DbContextOptionsBuilder<CaregiversDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new CaregiversDbContext(options);
    }
}