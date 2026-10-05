using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Earnings;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverEarningsAuthorizationTests
{
    [Fact]
    public async Task Summary_FailsWhenActorDoesNotOwnCaregiverProfile()
    {
        await using var caregiversDb = CreateCaregiversDb();
        await using var familiesDb = CreateFamiliesDb();
        var caregiver = Caregiver.Create(UserId.New(), CaregiverType.Medical);
        caregiversDb.Caregivers.Add(caregiver);
        await caregiversDb.SaveChangesAsync();

        var result = await new GetCaregiverEarningsSummaryQueryHandler(caregiversDb, familiesDb)
            .Handle(new GetCaregiverEarningsSummaryQuery(caregiver.Id.Value, UserId.New()), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Caregivers.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Transactions_FailsWhenActorDoesNotOwnCaregiverProfile()
    {
        await using var caregiversDb = CreateCaregiversDb();
        await using var familiesDb = CreateFamiliesDb();
        var caregiver = Caregiver.Create(UserId.New(), CaregiverType.Medical);
        caregiversDb.Caregivers.Add(caregiver);
        await caregiversDb.SaveChangesAsync();

        var result = await new GetCaregiverEarningsTransactionsQueryHandler(caregiversDb, familiesDb)
            .Handle(new GetCaregiverEarningsTransactionsQuery(caregiver.Id.Value, UserId.New()), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Caregivers.NotFound", result.Error.Code);
    }

    private static CaregiversDbContext CreateCaregiversDb() => new(
        new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static FamiliesDbContext CreateFamiliesDb() => new(
        new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
