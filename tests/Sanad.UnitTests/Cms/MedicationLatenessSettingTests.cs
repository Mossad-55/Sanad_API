using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Cms.Application.MedicationLateness;
using Sanad.Modules.Cms.Domain.MedicationLateness;
using Sanad.Modules.Cms.Infrastructure.Persistence;

namespace Sanad.UnitTests.Cms;

public sealed class MedicationLatenessSettingTests
{
    [Fact]
    public async Task FirstRevision_UsesApprovedSixtyMinuteDefaultAndIsActive()
    {
        await using var db = CreateDb();

        var result = await new CreateMedicationLatenessSettingRevisionCommandHandler(db)
            .Handle(new(MedicationLatenessSetting.InitialThresholdMinutes), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.ThresholdMinutes);
        Assert.Equal(1, result.Value.Version);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task NewRevision_DeactivatesPreviousAndLeavesItImmutable()
    {
        await using var db = CreateDb();
        var handler = new CreateMedicationLatenessSettingRevisionCommandHandler(db);

        var first = await handler.Handle(new(60), default);
        var second = await handler.Handle(new(90), default);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, second.Value.Version);
        Assert.Equal(90, (await new GetMedicationLatenessSettingQueryHandler(db).Handle(new(), default)).Value.ThresholdMinutes);
        Assert.Equal(2, await db.MedicationLatenessSettingRevisions.CountAsync());
        Assert.Single(await db.MedicationLatenessSettingRevisions.Where(x => x.IsActive).ToListAsync());

        var old = await db.MedicationLatenessSettingRevisions.SingleAsync(x => x.Version == 1);
        old.ThresholdMinutesViaTestOnly();
    }

    [Fact]
    public async Task RevisionMutation_IsRejectedByPersistenceGuard()
    {
        await using var db = CreateDb();
        var setting = MedicationLatenessSetting.Create();
        var revision = setting.AddRevision(60);
        revision.Activate();
        db.MedicationLatenessSettings.Add(setting);
        await db.SaveChangesAsync();

        db.Entry(revision).Property(x => x.ThresholdMinutes).CurrentValue = 90;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private static CmsDbContext CreateDb() => new(new DbContextOptionsBuilder<CmsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}

file static class MedicationLatenessRevisionTestExtensions
{
    // Compile-time proof that the old revision remains queryable after activation of a new one.
    public static void ThresholdMinutesViaTestOnly(this MedicationLatenessSettingRevision revision)
        => Assert.Equal(60, revision.ThresholdMinutes);
}
