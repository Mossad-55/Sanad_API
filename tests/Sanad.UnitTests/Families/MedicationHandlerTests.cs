using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Medications;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Medications;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families;

public sealed class MedicationHandlerTests
{
    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }

    [Fact]
    public async Task AddMedicationHandler_WithValidData_PersistsAndReturnsResponse()
    {
        using var db = CreateDbContext();
        var userId = UserId.New();
        var family = Family.Create(userId, "Al-Mansour Family");
        var elderly = Elderly.Create(
            userId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Father,
            FullName.Create("أحمد"),
            FullName.Create("Ahmed"),
            Gender.Male,
            new DateOnly(1955, 1, 1),
            DateOnly.FromDateTime(DateTime.UtcNow));

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        await db.SaveChangesAsync();

        var handler = new AddMedicationCommandHandler(db);
        var command = new AddMedicationCommand(
            userId,
            elderly.Id,
            "Aspirin Protect",
            "100 mg",
            "قرص",
            1,
            new[] { new TimeOnly(8, 0) },
            new DateOnly(2026, 9, 1),
            null,
            "بعد الإفطار",
            30,
            5);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Aspirin Protect", result.Value.Name);
        Assert.Equal(30, result.Value.StockQuantity);
        Assert.Equal(StockStatus.Normal, result.Value.StockStatus);
    }

    [Fact]
    public async Task RecordDoseTaken_DecrementsStock_AndRecordsLog()
    {
        using var db = CreateDbContext();
        var userId = UserId.New();
        var family = Family.Create(userId, "Al-Mansour Family");
        var elderly = Elderly.Create(
            userId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Father,
            FullName.Create("أحمد"),
            FullName.Create("Ahmed"),
            Gender.Male,
            new DateOnly(1955, 1, 1),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var med = Medication.Create(
            elderly.Id,
            userId,
            "Concor",
            "5 mg",
            "قرص",
            1,
            new[] { new TimeOnly(9, 0) },
            new DateOnly(2026, 9, 1),
            null,
            stockQuantity: 10,
            lowStockThreshold: 3);

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.Medications.Add(med);
        await db.SaveChangesAsync();

        var handler = new RecordDoseTakenCommandHandler(db);
        var command = new RecordDoseTakenCommand(
            userId,
            elderly.Id,
            med.Id,
            new DateOnly(2026, 9, 3),
            new TimeOnly(9, 0),
            "تم التناول",
            DateTime.UtcNow);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DoseStatus.Taken, result.Value.Status);

        var reloadedMed = await db.Medications.FindAsync(med.Id);
        Assert.Equal(9, reloadedMed!.StockQuantity);
    }
}