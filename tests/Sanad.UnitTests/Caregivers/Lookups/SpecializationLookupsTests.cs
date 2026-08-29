using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class SpecializationLookupsTests
{
    [Fact]
    public async Task Create_ShouldPersistInactive_WhenFlagFalse()
    {
        using var db = CreateDbContext();
        var result = await new CreateSpecializationCommandHandler(db).Handle(
            new CreateSpecializationCommand("تمريض", "Nursing", CaregiverType.Medical, false), default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsActive);
    }

    [Fact]
    public async Task Create_ShouldRejectDuplicateWithinSameType()
    {
        using var db = CreateDbContext();
        var handler = new CreateSpecializationCommandHandler(db);
        await handler.Handle(new CreateSpecializationCommand("تمريض", "Nursing", CaregiverType.Medical, true), default);

        var duplicate = await handler.Handle(
            new CreateSpecializationCommand("تمريض", "Nursing", CaregiverType.Medical, true), default);

        Assert.True(duplicate.IsFailure);
        Assert.Equal(LookupsErrors.NameAlreadyInUse, duplicate.Error);
    }

    [Fact]
    public async Task Create_ShouldAllowSameNameAcrossTypes()
    {
        using var db = CreateDbContext();
        var handler = new CreateSpecializationCommandHandler(db);
        var medical = await handler.Handle(new CreateSpecializationCommand("رعاية", "Care", CaregiverType.Medical, true), default);
        var companion = await handler.Handle(new CreateSpecializationCommand("رعاية", "Care", CaregiverType.Companion, true), default);

        Assert.True(medical.IsSuccess);
        Assert.True(companion.IsSuccess);
    }

    [Fact]
    public async Task GetActive_ShouldOnlyReturnActive()
    {
        using var db = CreateDbContext();
        var handler = new CreateSpecializationCommandHandler(db);
        await handler.Handle(new CreateSpecializationCommand("تمريض", "Nursing", CaregiverType.Medical, true), default);
        var hidden = await handler.Handle(new CreateSpecializationCommand("علاج", "Therapy", CaregiverType.Medical, false), default);

        var result = await new GetActiveSpecializationsQueryHandler(db).Handle(
            new GetActiveSpecializationsQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
    }

    private static CaregiversDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new CaregiversDbContext(options);
    }
}