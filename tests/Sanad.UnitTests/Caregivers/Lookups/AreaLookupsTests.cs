using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class AreaLookupsTests
{
    [Fact]
    public async Task Create_ShouldPersistAreaAsActive()
    {
        using CaregiversDbContext db = CreateDbContext();
        var city = await SeedCityAsync(db, cityActive: true, governorateActive: true);

        var result =
            await new CreateAreaCommandHandler(db).Handle(
                new CreateAreaCommand(city.Id, "مركز دمنهور", "Damanhur Markaz"),
                default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task Create_ShouldReturnParentNotFound_WhenCityMissing()
    {
        using CaregiversDbContext db = CreateDbContext();

        var result =
            await new CreateAreaCommandHandler(db).Handle(
                new CreateAreaCommand(CityId.New(), "مركز", "Markaz"),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(LookupsErrors.ParentNotFound, result.Error);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Create_ShouldReturnParentNotActive_WhenChainInactive(
        bool cityActive,
        bool governorateActive)
    {
        using CaregiversDbContext db = CreateDbContext();
        var city = await SeedCityAsync(db, cityActive, governorateActive);

        var result =
            await new CreateAreaCommandHandler(db).Handle(
                new CreateAreaCommand(city.Id, "مركز", "Markaz"),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(LookupsErrors.ParentNotActive, result.Error);
    }

    [Fact]
    public async Task Create_ShouldRejectDuplicateNameWithinCity()
    {
        using CaregiversDbContext db = CreateDbContext();
        var city = await SeedCityAsync(db, cityActive: true, governorateActive: true);
        var handler = new CreateAreaCommandHandler(db);

        await handler.Handle(
            new CreateAreaCommand(city.Id, "مركز دمنهور", "Damanhur Markaz"), default);

        var duplicate =
            await handler.Handle(
                new CreateAreaCommand(city.Id, "مركز دمنهور", "Damanhur Markaz"), default);

        Assert.True(duplicate.IsFailure);
        Assert.Equal(LookupsErrors.NameAlreadyInUse, duplicate.Error);
    }

    [Fact]
    public async Task GetActive_ShouldRequireFullActiveChain()
    {
        using CaregiversDbContext db = CreateDbContext();
        var city = await SeedCityAsync(db, cityActive: true, governorateActive: true);
        var area = await new CreateAreaCommandHandler(db).Handle(
            new CreateAreaCommand(city.Id, "أبو الريش", "Abu El Rish"), default);

        // deactivate the city -> area must vanish publicly but remain in admin list
        await new SetCityActiveCommandHandler(db).Handle(
            new SetCityActiveCommand(city.Id, false), default);

        var publicResult =
            await new GetActiveAreasQueryHandler(db).Handle(
                new GetActiveAreasQuery(city.Id), default);
        Assert.Empty(publicResult.Value);

        var adminResult =
            await new GetAllAreasQueryHandler(db).Handle(
                new GetAllAreasQuery(city.Id), default);

        Assert.Single(adminResult.Value);
        Assert.True(adminResult.Value[0].IsActive);
    }

    private static async Task<City> SeedCityAsync(
        CaregiversDbContext db,
        bool cityActive,
        bool governorateActive)
    {
        var governorate =
            Governorate.Create(
                $"محافظة {Guid.NewGuid():N}",
                $"Gov {Guid.NewGuid():N}");

        if (!governorateActive)
        {
            governorate.Deactivate();
        }

        db.Governorates.Add(governorate);

        var city =
            City.Create(
                governorate.Id,
                $"مدينة {Guid.NewGuid():N}",
                $"City {Guid.NewGuid():N}");

        if (!cityActive)
        {
            city.Deactivate();
        }

        db.Cities.Add(city);
        await db.SaveChangesAsync();
        return city;
    }

    private static CaregiversDbContext CreateDbContext()
    {
        var options =
            new DbContextOptionsBuilder<CaregiversDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new CaregiversDbContext(options);
    }
}