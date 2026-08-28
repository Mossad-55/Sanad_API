using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class CityLookupsTests
{
    [Fact]
    public async Task Create_ShouldPersistCityAsActive()
    {
        using CaregiversDbContext db = CreateDbContext();
        var governorate = await SeedGovernorateAsync(db, active: true);

        var result =
            await new CreateCityCommandHandler(db).Handle(
                new CreateCityCommand(governorate.Id, "دمنهور", "Damanhur"),
                default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
        Assert.Equal(governorate.Id, result.Value.GovernorateId);
    }

    [Fact]
    public async Task Create_ShouldReturnParentNotFound_WhenGovernorateMissing()
    {
        using CaregiversDbContext db = CreateDbContext();

        var result =
            await new CreateCityCommandHandler(db).Handle(
                new CreateCityCommand(GovernorateId.New(), "دمنهور", "Damanhur"),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(LookupsErrors.ParentNotFound, result.Error);
    }

    [Fact]
    public async Task Create_ShouldReturnParentNotActive_WhenGovernorateInactive()
    {
        using CaregiversDbContext db = CreateDbContext();
        var governorate = await SeedGovernorateAsync(db, active: false);

        var result =
            await new CreateCityCommandHandler(db).Handle(
                new CreateCityCommand(governorate.Id, "دمنهور", "Damanhur"),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(LookupsErrors.ParentNotActive, result.Error);
    }

    [Fact]
    public async Task Create_ShouldRejectDuplicateNameWithinGovernorate()
    {
        using CaregiversDbContext db = CreateDbContext();
        var governorate = await SeedGovernorateAsync(db, active: true);
        var handler = new CreateCityCommandHandler(db);

        await handler.Handle(
            new CreateCityCommand(governorate.Id, "دمنهور", "Damanhur"), default);

        var duplicate =
            await handler.Handle(
                new CreateCityCommand(governorate.Id, "دمنهور", "Damanhur"), default);

        Assert.True(duplicate.IsFailure);
        Assert.Equal(LookupsErrors.NameAlreadyInUse, duplicate.Error);
    }

    [Fact]
    public async Task GetActive_ShouldHideCitiesUnderInactiveGovernorate()
    {
        using CaregiversDbContext db = CreateDbContext();
        var activeGov = await SeedGovernorateAsync(db, active: true);
        var inactiveGov = await SeedGovernorateAsync(db, active: true);
        var create = new CreateCityCommandHandler(db);

        await create.Handle(new CreateCityCommand(activeGov.Id, "القاهرة", "Cairo"), default);
        var hidden = await create.Handle(
            new CreateCityCommand(inactiveGov.Id, "الجيزة", "Giza"), default);
        await new SetGovernorateActiveCommandHandler(db).Handle(
            new SetGovernorateActiveCommand(inactiveGov.Id, false), default);

        var result =
            await new GetActiveCitiesQueryHandler(db).Handle(
                new GetActiveCitiesQuery(inactiveGov.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);

        var activeResult =
            await new GetActiveCitiesQueryHandler(db).Handle(
                new GetActiveCitiesQuery(activeGov.Id), default);
        Assert.Single(activeResult.Value);
    }

    private static async Task<Governorate> SeedGovernorateAsync(
        CaregiversDbContext db,
        bool active)
    {
        var governorate =
            Governorate.Create(
                $"محافظة {Guid.NewGuid():N}",
                $"Gov {Guid.NewGuid():N}");

        if (!active)
        {
            governorate.Deactivate();
        }

        db.Governorates.Add(governorate);
        await db.SaveChangesAsync();
        return governorate;
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