using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.MedicationTasks;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers.MedicationTasks;

public sealed class CaregiverMedicationTaskHandlerValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Skip_RejectsMissingReasonBeforeAuthorizationLookup(string reason)
    {
        await using var db = CreateDb();
        await using var caregiversDb = CreateCaregiversDb();
        var result = await new SkipCaregiverMedicationTaskCommandHandler(caregiversDb, db)
            .Handle(new SkipCaregiverMedicationTaskCommand(Guid.NewGuid(), MedicationDoseLogId.New(), UserId.New(), reason), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Caregivers.MedicationTask.InvalidReason", result.Error.Code);
    }

    [Fact]
    public async Task Administer_MissingTaskDoesNotMutateDatabase()
    {
        await using var db = CreateDb();
        await using var caregiversDb = CreateCaregiversDb();
        var result = await new AdministerCaregiverMedicationTaskCommandHandler(caregiversDb, db)
            .Handle(new AdministerCaregiverMedicationTaskCommand(Guid.NewGuid(), MedicationDoseLogId.New(), UserId.New(), null), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Caregivers.AccessDenied", result.Error.Code);
        Assert.Empty(db.MedicationDoseLogs);
    }

    private static FamiliesDbContext CreateDb() => new(
        new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static CaregiversDbContext CreateCaregiversDb() => new(
        new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
