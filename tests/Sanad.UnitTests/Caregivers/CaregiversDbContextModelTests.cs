using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Domain.Caregivers.Selections;
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

    [Fact]
    public void Model_ShouldMapCaregiversTable()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        bool exists = dbContext.Model.GetEntityTypes()
            .Any(t => t.GetTableName() == "caregivers");

        Assert.True(exists);
    }

    [Fact]
    public void Model_ShouldIgnoreComputedShiftProperties()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var entityType = dbContext.Model.FindEntityType(
            typeof(MedicalShiftAvailability));

        Assert.NotNull(entityType);
        Assert.DoesNotContain(entityType!.GetProperties(),
            p => p.Name is "StartTime" or "EndTime" or "Duration" or "EndsNextDay");
    }

    [Fact]
    public void Model_ShouldUseCompositeKeyForServiceSelections()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var entityType = dbContext.Model.FindEntityType(
            typeof(CaregiverServiceSelection));

        Assert.NotNull(entityType);
        var key = entityType!.FindPrimaryKey();
        Assert.NotNull(key);
        Assert.Equal(2, key!.Properties.Count);
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