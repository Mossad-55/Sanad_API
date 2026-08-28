using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class GovernorateLookupsTests
{
    [Fact]
    public async Task Create_ShouldPersistGovernorateAsActive()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var handler = new CreateGovernorateCommandHandler(dbContext);

        var result =
            await handler.Handle(
                new CreateGovernorateCommand(
                    "البحيرة",
                    "Beheira"),
                default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
        Assert.Equal("Beheira", result.Value.EnglishName);
    }

    [Fact]
    public async Task Create_ShouldRejectDuplicateName()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var handler = new CreateGovernorateCommandHandler(dbContext);

        await handler.Handle(
            new CreateGovernorateCommand("القاهرة", "Cairo"),
            default);

        var result =
            await handler.Handle(
                new CreateGovernorateCommand("القاهرة", "Cairo"),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            LookupsErrors.NameAlreadyInUse,
            result.Error);
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