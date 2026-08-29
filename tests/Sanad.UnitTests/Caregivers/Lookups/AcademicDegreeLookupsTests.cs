using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class AcademicDegreeLookupsTests
{
    [Fact]
    public async Task Create_ShouldPersistDegree()
    {
        using var db = CreateDbContext();
        var result = await new CreateAcademicDegreeCommandHandler(db).Handle(
            new CreateAcademicDegreeCommand("بكالوريوس", "Bachelor", true), default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task Create_ShouldRejectDuplicateName()
    {
        using var db = CreateDbContext();
        var handler = new CreateAcademicDegreeCommandHandler(db);
        await handler.Handle(new CreateAcademicDegreeCommand("بكالوريوس", "Bachelor", true), default);

        var duplicate = await handler.Handle(
            new CreateAcademicDegreeCommand("بكالوريوس", "Bachelor", true), default);

        Assert.True(duplicate.IsFailure);
        Assert.Equal(LookupsErrors.NameAlreadyInUse, duplicate.Error);
    }

    private static CaregiversDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new CaregiversDbContext(options);
    }
}