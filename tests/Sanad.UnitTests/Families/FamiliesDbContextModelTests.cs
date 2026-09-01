using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class FamiliesDbContextModelTests
{
    [Fact]
    public void Model_ShouldUseFamiliesSchema()
    {
        using FamiliesDbContext dbContext = CreateDbContext();

        Assert.Equal(
            FamiliesDbContext.Schema,
            dbContext.Model.GetDefaultSchema());
    }

    [Fact]
    public void Model_ShouldMapUniqueElderlyIdentityUserId()
    {
        using FamiliesDbContext dbContext = CreateDbContext();

        var entityType = dbContext.Model.FindEntityType(
            typeof(Elderly));

        Assert.NotNull(entityType);

        Assert.Contains(
            entityType!.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties.Count == 1 &&
                index.Properties[0].Name ==
                    nameof(Elderly.IdentityUserId));
    }

    [Fact]
    public void ElderliesDbSet_ShouldPersistAndReloadElderly()
    {
        string databaseName = Guid.NewGuid().ToString();

        Family family = Family.Create(UserId.New(), "My Family");

        Elderly elderly = Elderly.Create(
            UserId.New(),
            UserId.New(),
            family.Id,
            Sanad.BuildingBlocks.Domain.ValueObjects.FullName.Create("أحمد"),
            Sanad.BuildingBlocks.Domain.ValueObjects.FullName.Create("Ahmed"),
            Sanad.BuildingBlocks.Domain.Enums.Gender.Male,
            new DateOnly(1950, 5, 1),
            DateOnly.FromDateTime(DateTime.UtcNow),
            detailedAddress: "12 Nile Street",
            healthNotes: "Diabetes");

        using (FamiliesDbContext arrangeContext =
                   CreateDbContext(databaseName))
        {
            arrangeContext.Families.Add(family);
            arrangeContext.Elderlies.Add(elderly);
            arrangeContext.SaveChanges();
        }

        using FamiliesDbContext assertContext =
            CreateDbContext(databaseName);

        Elderly? reloaded = assertContext.Elderlies
            .AsNoTracking()
            .SingleOrDefault(e => e.Id == elderly.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(elderly.IdentityUserId, reloaded!.IdentityUserId);
        Assert.Equal(family.Id, reloaded.FamilyId);
        Assert.Equal("Diabetes", reloaded.HealthNotes);
        Assert.Equal("12 Nile Street", reloaded.DetailedAddress);
    }

    private static FamiliesDbContext CreateDbContext(
        string? databaseName = null)
    {
        DbContextOptionsBuilder<FamiliesDbContext> options =
            new();

        options.UseInMemoryDatabase(
            databaseName ?? Guid.NewGuid().ToString());

        return new FamiliesDbContext(options.Options);
    }
}