using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Medications;
using Sanad.Modules.Families.Application.Medications;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Medications;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class MedicationLatenessEvaluationTests
{
    [Fact]
    public async Task Evaluation_UsesSixtyMinuteBoundaryAndMaterializesMissedDose()
    {
        await using var db = CreateDb(out var elderly, out var medication);
        elderly.ChangeTimeZone("UTC");
        var alerts = new RecordingAlerts();
        var handler = new EvaluateOwnMedicationLatenessCommandHandler(db, new Setting(60, 1), alerts);

        var before = await handler.Handle(new(elderly.IdentityUserId, Utc(2026, 9, 10, 8, 59)), default);
        var atBoundary = await handler.Handle(new(elderly.IdentityUserId, Utc(2026, 9, 10, 9, 0)), default);

        Assert.True(before.IsSuccess);
        Assert.Empty(before.Value.MissedDoses);
        Assert.True(atBoundary.IsSuccess);
        var dose = Assert.Single(atBoundary.Value.MissedDoses);
        Assert.Equal(medication.Id.Value, dose.MedicationId);
        Assert.Equal(DoseStatus.Missed, dose.Status);
        Assert.Single(await db.MedicationDoseLogs.ToListAsync());
        Assert.Single(alerts.Requests);
    }

    [Fact]
    public async Task Evaluation_UsesProfileLocalDayAndDoesNotRecalculateHistoricalDay()
    {
        await using var db = CreateDb(out var elderly, out _);
        elderly.ChangeTimeZone("Asia/Tokyo");
        var alerts = new RecordingAlerts();
        var handler = new EvaluateOwnMedicationLatenessCommandHandler(db, new Setting(60, 4), alerts);

        var result = await handler.Handle(new(elderly.IdentityUserId, Utc(2026, 9, 11, 0, 30)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 9, 11), result.Value.LocalDate);
        Assert.Single(result.Value.MissedDoses);
        Assert.Equal(new DateOnly(2026, 9, 11), Assert.Single(await db.MedicationDoseLogs.ToListAsync()).ScheduledDate);
        Assert.Equal(4, result.Value.SettingVersion);
    }

    [Fact]
    public async Task OwnEvaluation_IsIdentityBoundAndAdminEvaluationRequiresExistingActiveFamily()
    {
        await using var db = CreateDb(out var elderly, out _);
        var alerts = new RecordingAlerts();
        var setting = new Setting(60, 1);

        var denied = await new EvaluateOwnMedicationLatenessCommandHandler(db, setting, alerts)
            .Handle(new(UserId.New(), Utc(2026, 9, 10, 9, 0)), default);
        var admin = await new EvaluateAdminMedicationLatenessCommandHandler(db, setting, alerts)
            .Handle(new(UserId.New(), elderly.Id, "SupportAdmin", "corr-1", Utc(2026, 9, 10, 9, 0)), default);

        Assert.False(denied.IsSuccess);
        Assert.Equal(MedicationErrors.AccessDenied, denied.Error);
        Assert.True(admin.IsSuccess);
        Assert.Equal(1, await db.AdminMedicationAccessAudits.CountAsync());
    }

    private static FamiliesDbContext CreateDb(out Elderly elderly, out Medication medication)
    {
        var db = new FamiliesDbContext(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var owner = UserId.New();
        var family = Family.Create(owner, "Medication family");
        elderly = Elderly.Create(owner, UserId.New(), family.Id, FamilyRelationshipType.Father,
            FullName.Create("Ahmed"), FullName.Create("Ahmed"), Gender.Male,
            new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        medication = Medication.Create(elderly.Id, owner, "Aspirin", "100 mg", "tablet", 1,
            new[] { new TimeOnly(8, 0) }, new DateOnly(2026, 1, 1), null, stockQuantity: 10, lowStockThreshold: 2);
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.Medications.Add(medication);
        db.SaveChanges();
        return db;
    }

    private static DateTime Utc(int y, int m, int d, int h, int min) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private sealed record Setting(int ThresholdMinutes, int Version) : IMedicationLatenessSettingGateway
    {
        public Task<MedicationLatenessSettingSnapshot?> GetActiveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<MedicationLatenessSettingSnapshot?>(new(ThresholdMinutes, Version));
    }

    private sealed class RecordingAlerts : IMedicationLateAlertGateway
    {
        public List<MedicationLateAlertRequest> Requests { get; } = [];
        public Task<int> CreateAsync(MedicationLateAlertRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(1);
        }
    }
}
