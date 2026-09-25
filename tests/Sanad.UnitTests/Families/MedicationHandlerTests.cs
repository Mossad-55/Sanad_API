using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
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

    [Fact]
    public async Task MedicationQueries_ForForeignFamilyDependent_ReturnAccessDenied_WithoutReturningData()
    {
        using var db = CreateForeignFamilyFixture(out var caller, out _, out var foreignElderly, out var foreignMedication, out var scheduledDate);

        var listResult = await new ListMedicationsQueryHandler(db).Handle(
            new(caller, foreignElderly.Id), CancellationToken.None);
        var detailResult = await new GetMedicationByIdQueryHandler(db).Handle(
            new(caller, foreignElderly.Id, foreignMedication.Id), CancellationToken.None);
        var dashboardResult = await new GetMedicationDashboardQueryHandler(db).Handle(
            new(caller, foreignElderly.Id, scheduledDate), CancellationToken.None);

        AssertAccessDenied(listResult);
        AssertAccessDenied(detailResult);
        AssertAccessDenied(dashboardResult);
    }

    [Fact]
    public async Task MedicationWrites_ForForeignFamilyDependent_ReturnAccessDenied_AndDoNotMutateMedicationOrDoseLog()
    {
        using var db = CreateForeignFamilyFixture(out var caller, out _, out var foreignElderly, out var foreignMedication, out var scheduledDate);
        var originalName = foreignMedication.Name;
        var originalStock = foreignMedication.StockQuantity;
        var originalStatus = foreignMedication.Status;

        var updateResult = await new UpdateMedicationCommandHandler(db).Handle(
            new(
                caller,
                foreignElderly.Id,
                foreignMedication.Id,
                "Changed by foreign family",
                "20 mg",
                "tablet",
                2,
                new[] { new TimeOnly(10, 0) },
                scheduledDate,
                null,
                "must not persist"),
            CancellationToken.None);
        var stockResult = await new UpdateMedicationStockCommandHandler(db).Handle(
            new(caller, foreignElderly.Id, foreignMedication.Id, 1, 0), CancellationToken.None);
        var pauseResult = await new StatusToggleCommandHandlers(db).Handle(
            new PauseMedicationCommand(caller, foreignElderly.Id, foreignMedication.Id), CancellationToken.None);
        var resumeResult = await new StatusToggleCommandHandlers(db).Handle(
            new ResumeMedicationCommand(caller, foreignElderly.Id, foreignMedication.Id), CancellationToken.None);
        var discontinueResult = await new StatusToggleCommandHandlers(db).Handle(
            new DiscontinueMedicationCommand(caller, foreignElderly.Id, foreignMedication.Id), CancellationToken.None);
        var takeResult = await new RecordDoseTakenCommandHandler(db).Handle(
            new(
                caller,
                foreignElderly.Id,
                foreignMedication.Id,
                scheduledDate,
                new TimeOnly(9, 0),
                "must not persist",
                DateTime.UtcNow),
            CancellationToken.None);
        var skipResult = await new RecordDoseSkippedCommandHandler(db).Handle(
            new(
                caller,
                foreignElderly.Id,
                foreignMedication.Id,
                scheduledDate,
                new TimeOnly(9, 0),
                "must not persist",
                DateTime.UtcNow),
            CancellationToken.None);

        AssertAccessDenied(updateResult);
        AssertAccessDenied(stockResult);
        AssertAccessDenied(pauseResult);
        AssertAccessDenied(resumeResult);
        AssertAccessDenied(discontinueResult);
        AssertAccessDenied(takeResult);
        AssertAccessDenied(skipResult);

        var persistedMedication = await db.Medications.SingleAsync(m => m.Id == foreignMedication.Id);
        Assert.Equal(originalName, persistedMedication.Name);
        Assert.Equal(originalStock, persistedMedication.StockQuantity);
        Assert.Equal(originalStatus, persistedMedication.Status);
        Assert.Empty(await db.MedicationDoseLogs
            .Where(log => log.MedicationId == foreignMedication.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task MedicationAccess_ForMissingDependent_ReturnsExpectedBoundaryErrors()
    {
        using var db = CreateDbContext();
        var caller = UserId.New();
        var family = Family.Create(caller, "Caller Family");
        db.Families.Add(family);
        await db.SaveChangesAsync();

        var missingDependent = ElderlyId.New();
        var queryResult = await new ListMedicationsQueryHandler(db).Handle(
            new(caller, missingDependent), CancellationToken.None);
        var addResult = await new AddMedicationCommandHandler(db).Handle(
            new(
                caller,
                missingDependent,
                "Missing dependent medication",
                "10 mg",
                "tablet",
                1,
                new[] { new TimeOnly(9, 0) },
                new DateOnly(2026, 9, 1),
                null,
                null,
                10,
                2),
            CancellationToken.None);

        AssertAccessDenied(queryResult);
        Assert.False(addResult.IsSuccess);
        Assert.Equal(MedicationErrors.DependentNotFound, addResult.Error);
        Assert.Empty(await db.Medications.ToListAsync());
    }

    [Fact]
    public async Task UpdateMedication_WithStockObject_PersistsDetailsAndInventoryTogether()
    {
        using var db = CreateOwnedMedicationFixture(out var userId, out var elderly, out var medication);

        var result = await new UpdateMedicationCommandHandler(db).Handle(
            new(
                userId,
                elderly.Id,
                medication.Id,
                "Updated Aspirin",
                "200 mg",
                "tablet",
                2,
                new[] { new TimeOnly(20, 0), new TimeOnly(8, 0) },
                new DateOnly(2026, 9, 2),
                new DateOnly(2026, 10, 2),
                "with food",
                42,
                7,
                true,
                true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var persisted = await db.Medications.AsNoTracking().SingleAsync(m => m.Id == medication.Id);
        Assert.Equal("Updated Aspirin", persisted.Name);
        Assert.Equal("200 mg", persisted.Dosage);
        Assert.Equal(2, persisted.DoseQuantity);
        Assert.Equal(new[] { new TimeOnly(8, 0), new TimeOnly(20, 0) }, persisted.DoseTimes);
        Assert.Equal(42, persisted.StockQuantity);
        Assert.Equal(7, persisted.LowStockThreshold);
    }

    [Fact]
    public async Task UpdateMedication_WithoutStockObject_PreservesExistingInventory()
    {
        using var db = CreateOwnedMedicationFixture(out var userId, out var elderly, out var medication);

        var result = await new UpdateMedicationCommandHandler(db).Handle(
            new(
                userId,
                elderly.Id,
                medication.Id,
                "Updated without stock",
                "15 mg",
                "capsule",
                1,
                new[] { new TimeOnly(11, 0) },
                new DateOnly(2026, 9, 4),
                null,
                null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var persisted = await db.Medications.AsNoTracking().SingleAsync(m => m.Id == medication.Id);
        Assert.Equal("Updated without stock", persisted.Name);
        Assert.Equal(10, persisted.StockQuantity);
        Assert.Equal(3, persisted.LowStockThreshold);
    }

    [Fact]
    public async Task UpdateMedication_WithExplicitNullStockObject_ClearsInventoryTracking()
    {
        using var db = CreateOwnedMedicationFixture(out var userId, out var elderly, out var medication);

        var result = await new UpdateMedicationCommandHandler(db).Handle(
            new(
                userId,
                elderly.Id,
                medication.Id,
                "Untracked medication",
                "5 mg",
                "tablet",
                1,
                new[] { new TimeOnly(12, 0) },
                new DateOnly(2026, 9, 5),
                null,
                null,
                null,
                null,
                true,
                true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var persisted = await db.Medications.AsNoTracking().SingleAsync(m => m.Id == medication.Id);
        Assert.Equal("Untracked medication", persisted.Name);
        Assert.Null(persisted.StockQuantity);
        Assert.Null(persisted.LowStockThreshold);
    }

    [Fact]
    public async Task UpdateMedication_WithNegativeStock_PersistsNeitherDetailsNorInventory()
    {
        using var db = CreateOwnedMedicationFixture(out var userId, out var elderly, out var medication);

        var result = await new UpdateMedicationCommandHandler(db).Handle(
            new(
                userId,
                elderly.Id,
                medication.Id,
                "Should not persist",
                "999 mg",
                "liquid",
                4,
                new[] { new TimeOnly(13, 0) },
                new DateOnly(2026, 9, 6),
                null,
                null,
                -1,
                2,
                true,
                true),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MedicationErrors.InvalidMedication, result.Error);
        var persisted = await db.Medications.AsNoTracking().SingleAsync(m => m.Id == medication.Id);
        Assert.Equal("Original medication", persisted.Name);
        Assert.Equal("10 mg", persisted.Dosage);
        Assert.Equal(10, persisted.StockQuantity);
        Assert.Equal(3, persisted.LowStockThreshold);
    }

    [Fact]
    public async Task UpdateMedication_WithInvalidDetails_PersistsNeitherDetailsNorInventory()
    {
        using var db = CreateOwnedMedicationFixture(out var userId, out var elderly, out var medication);

        var result = await new UpdateMedicationCommandHandler(db).Handle(
            new(
                userId,
                elderly.Id,
                medication.Id,
                "Should not persist",
                "999 mg",
                "liquid",
                4,
                new[] { new TimeOnly(13, 0) },
                new DateOnly(2026, 9, 8),
                new DateOnly(2026, 9, 7),
                null,
                99,
                1,
                true,
                true),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MedicationErrors.InvalidMedication, result.Error);
        var persisted = await db.Medications.AsNoTracking().SingleAsync(m => m.Id == medication.Id);
        Assert.Equal("Original medication", persisted.Name);
        Assert.Equal("10 mg", persisted.Dosage);
        Assert.Equal(10, persisted.StockQuantity);
        Assert.Equal(3, persisted.LowStockThreshold);
    }

    [Fact]
    public async Task UpdateMedication_WithIncompleteStockObject_IsRejectedBeforeMutation()
    {
        using var db = CreateOwnedMedicationFixture(out var userId, out var elderly, out var medication);

        var result = await new UpdateMedicationCommandHandler(db).Handle(
            new(
                userId,
                elderly.Id,
                medication.Id,
                "Should not persist",
                "20 mg",
                "tablet",
                1,
                new[] { new TimeOnly(10, 0) },
                new DateOnly(2026, 9, 9),
                null,
                null,
                8,
                null,
                true,
                false),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MedicationErrors.InvalidMedication, result.Error);
        var persisted = await db.Medications.AsNoTracking().SingleAsync(m => m.Id == medication.Id);
        Assert.Equal("Original medication", persisted.Name);
        Assert.Equal(10, persisted.StockQuantity);
        Assert.Equal(3, persisted.LowStockThreshold);
    }

    [Fact]
    public async Task UpdateMedication_WithCountAndNoThreshold_IsAllowedWhenBothFieldsWereProvided()
    {
        using var db = CreateOwnedMedicationFixture(out var userId, out var elderly, out var medication);

        var result = await new UpdateMedicationCommandHandler(db).Handle(
            new(
                userId,
                elderly.Id,
                medication.Id,
                "Count tracked without alert",
                "10 mg",
                "tablet",
                1,
                new[] { new TimeOnly(9, 0) },
                new DateOnly(2026, 9, 9),
                null,
                null,
                25,
                null,
                true,
                true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var persisted = await db.Medications.AsNoTracking().SingleAsync(m => m.Id == medication.Id);
        Assert.Equal(25, persisted.StockQuantity);
        Assert.Null(persisted.LowStockThreshold);
    }

    [Fact]
    public async Task GetMedicationDoseHistory_ReturnsPersistedLogsInInclusiveChronologicalOrder()
    {
        using var db = CreateOwnedMedicationFixture(out var userId, out var elderly, out var medication);
        var start = new DateOnly(2026, 9, 10);
        var end = new DateOnly(2026, 9, 11);
        var takenAt = new DateTime(2026, 9, 10, 9, 5, 0, DateTimeKind.Utc);
        var skippedAt = new DateTime(2026, 9, 11, 8, 2, 0, DateTimeKind.Utc);
        var early = MedicationDoseLog.CreateScheduled(medication.Id, elderly.Id, start, new TimeOnly(8, 0));
        early.MarkAsTaken(userId, takenAt, "taken notes");
        var laterSameDay = MedicationDoseLog.CreateScheduled(medication.Id, elderly.Id, start, new TimeOnly(20, 0));
        laterSameDay.MarkAsSkipped(userId, skippedAt, "skipped notes");
        var lastBoundary = MedicationDoseLog.CreateScheduled(medication.Id, elderly.Id, end, new TimeOnly(7, 0));
        var outside = MedicationDoseLog.CreateScheduled(medication.Id, elderly.Id, end.AddDays(1), new TimeOnly(7, 0));
        db.MedicationDoseLogs.AddRange(early, laterSameDay, lastBoundary, outside);
        await db.SaveChangesAsync();

        var result = await new GetMedicationDoseHistoryQueryHandler(db).Handle(
            new(userId, elderly.Id, medication.Id, start, end), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(result.Value,
            dose =>
            {
                Assert.Equal(start, dose.ScheduledDate);
                Assert.Equal(new TimeOnly(8, 0), dose.ScheduledTime);
                Assert.Equal(DoseStatus.Taken, dose.Status);
                Assert.Equal(takenAt, dose.TakenAtUtc);
                Assert.Null(dose.SkippedAtUtc);
                Assert.Equal("taken notes", dose.Notes);
                Assert.Equal(userId.Value, dose.LoggedByUserId);
                Assert.Equal(medication.Id.Value, dose.MedicationId);
                Assert.Equal(medication.Name, dose.MedicationName);
            },
            dose =>
            {
                Assert.Equal(start, dose.ScheduledDate);
                Assert.Equal(new TimeOnly(20, 0), dose.ScheduledTime);
                Assert.Equal(DoseStatus.Skipped, dose.Status);
                Assert.Equal(skippedAt, dose.SkippedAtUtc);
                Assert.Null(dose.TakenAtUtc);
                Assert.Equal("skipped notes", dose.Notes);
                Assert.Equal(userId.Value, dose.LoggedByUserId);
            },
            dose =>
            {
                Assert.Equal(end, dose.ScheduledDate);
                Assert.Equal(DoseStatus.Scheduled, dose.Status);
                Assert.Null(dose.TakenAtUtc);
                Assert.Null(dose.SkippedAtUtc);
                Assert.Null(dose.Notes);
                Assert.Null(dose.LoggedByUserId);
            });
    }

    [Fact]
    public async Task GetMedicationDoseHistory_FiltersByMedicationAndDependent_AndReturnsOnlyPersistedLogs()
    {
        using var db = CreateOwnedMedicationFixture(out var userId, out var elderly, out var medication);
        var anotherMedication = Medication.Create(
            elderly.Id, userId, "Other medication", "2 mg", "tablet", 1,
            new[] { new TimeOnly(12, 0) }, new DateOnly(2026, 9, 1), null);
        var otherDependent = Elderly.Create(
            userId, UserId.New(), db.Families.Single().Id, FamilyRelationshipType.Mother,
            FullName.Create("Other"), FullName.Create("Other"), Gender.Female,
            new DateOnly(1956, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow));
        db.Elderlies.Add(otherDependent);
        db.Medications.Add(anotherMedication);
        var date = new DateOnly(2026, 9, 10);
        db.MedicationDoseLogs.AddRange(
            MedicationDoseLog.CreateScheduled(medication.Id, elderly.Id, date, new TimeOnly(9, 0)),
            MedicationDoseLog.CreateScheduled(anotherMedication.Id, elderly.Id, date, new TimeOnly(10, 0)),
            MedicationDoseLog.CreateScheduled(medication.Id, otherDependent.Id, date, new TimeOnly(11, 0)));
        await db.SaveChangesAsync();

        var result = await new GetMedicationDoseHistoryQueryHandler(db).Handle(
            new(userId, elderly.Id, medication.Id, date, date), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dose = Assert.Single(result.Value);
        Assert.Equal(medication.Id.Value, dose.MedicationId);
        Assert.Equal(date, dose.ScheduledDate);
        Assert.Equal(new TimeOnly(9, 0), dose.ScheduledTime);
    }

    [Theory]
    [InlineData(2026, 9, 11, 2026, 9, 10)]
    [InlineData(2026, 9, 1, 2026, 10, 2)]
    public async Task GetMedicationDoseHistory_RejectsReversedOrOverlongDateRange(
        int startYear, int startMonth, int startDay, int endYear, int endMonth, int endDay)
    {
        using var db = CreateOwnedMedicationFixture(out var userId, out var elderly, out var medication);

        var result = await new GetMedicationDoseHistoryQueryHandler(db).Handle(
            new(userId, elderly.Id, medication.Id,
                new DateOnly(startYear, startMonth, startDay),
                new DateOnly(endYear, endMonth, endDay)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MedicationErrors.InvalidDateRange, result.Error);
    }

    [Fact]
    public async Task GetMedicationDoseHistory_DeniesForeignDependent_AndReturnsNotFoundForMissingMedication()
    {
        using var db = CreateForeignFamilyFixture(out var caller, out _, out var foreignElderly, out var foreignMedication, out var date);

        var denied = await new GetMedicationDoseHistoryQueryHandler(db).Handle(
            new(caller, foreignElderly.Id, foreignMedication.Id, date, date), CancellationToken.None);
        using var ownedDb = CreateOwnedMedicationFixture(out var owner, out var elderly, out _);
        var missing = await new GetMedicationDoseHistoryQueryHandler(ownedDb).Handle(
            new(owner, elderly.Id, MedicationId.New(), date, date), CancellationToken.None);

        AssertAccessDenied(denied);
        Assert.False(missing.IsSuccess);
        Assert.Equal(MedicationErrors.NotFound, missing.Error);
    }

    [Fact]
    public async Task GetMedicationDoseHistory_AllowsFamilyViewerToRead()
    {
        using var db = CreateOwnedMedicationFixture(out var owner, out var elderly, out var medication);
        var viewer = UserId.New();
        db.Families.Single().AddMember(FamilyMember.Create(
            viewer, owner, FamilyRelationshipType.Other, FamilyRole.Viewer));
        var date = new DateOnly(2026, 9, 10);
        db.MedicationDoseLogs.Add(MedicationDoseLog.CreateScheduled(
            medication.Id, elderly.Id, date, new TimeOnly(9, 0)));
        await db.SaveChangesAsync();

        var result = await new GetMedicationDoseHistoryQueryHandler(db).Handle(
            new(viewer, elderly.Id, medication.Id, date, date), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
    }

    private static FamiliesDbContext CreateForeignFamilyFixture(
        out UserId caller,
        out Family callerFamily,
        out Elderly foreignElderly,
        out Medication foreignMedication,
        out DateOnly scheduledDate)
    {
        var db = CreateDbContext();
        caller = UserId.New();
        callerFamily = Family.Create(caller, "Caller Family");
        var foreignOwner = UserId.New();
        var foreignFamily = Family.Create(foreignOwner, "Foreign Family");
        foreignElderly = Elderly.Create(
            foreignOwner,
            UserId.New(),
            foreignFamily.Id,
            FamilyRelationshipType.Father,
            FullName.Create("Ø£Ø­Ù…Ø¯"),
            FullName.Create("Ahmed"),
            Gender.Male,
            new DateOnly(1955, 1, 1),
            DateOnly.FromDateTime(DateTime.UtcNow));
        scheduledDate = new DateOnly(2026, 9, 3);
        foreignMedication = Medication.Create(
            foreignElderly.Id,
            foreignOwner,
            "Foreign Concor",
            "5 mg",
            "tablet",
            1,
            new[] { new TimeOnly(9, 0) },
            new DateOnly(2026, 9, 1),
            null,
            stockQuantity: 10,
            lowStockThreshold: 3);

        db.Families.AddRange(callerFamily, foreignFamily);
        db.Elderlies.Add(foreignElderly);
        db.Medications.Add(foreignMedication);
        db.SaveChanges();
        return db;
    }

    private static FamiliesDbContext CreateOwnedMedicationFixture(
        out UserId userId,
        out Elderly elderly,
        out Medication medication)
    {
        var db = CreateDbContext();
        userId = UserId.New();
        var family = Family.Create(userId, "Medication Family");
        elderly = Elderly.Create(
            userId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Father,
            FullName.Create("Original"),
            FullName.Create("Original"),
            Gender.Male,
            new DateOnly(1955, 1, 1),
            DateOnly.FromDateTime(DateTime.UtcNow));
        medication = Medication.Create(
            elderly.Id,
            userId,
            "Original medication",
            "10 mg",
            "tablet",
            1,
            new[] { new TimeOnly(9, 0) },
            new DateOnly(2026, 9, 1),
            null,
            stockQuantity: 10,
            lowStockThreshold: 3);

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.Medications.Add(medication);
        db.SaveChanges();
        return db;
    }

    private static void AssertAccessDenied(Result result)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(MedicationErrors.AccessDenied, result.Error);
    }
}
