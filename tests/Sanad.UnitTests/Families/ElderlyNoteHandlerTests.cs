using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Activities;
using Sanad.Modules.Families.Application.Notes;
using Sanad.Modules.Families.Domain.Activities;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Notes;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class ElderlyNoteHandlerTests
{
    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }

    [Fact]
    public async Task AddElderlyNote_PersistsNote_AndLogsActivity()
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

        var handler = new AddElderlyNoteCommandHandler(db);
        var command = new AddElderlyNoteCommand(
            userId,
            elderly.Id,
            "انخفاض الشهية",
            "تناول نصف وجبة الغداء فقط",
            NoteCategory.Nutrition,
            NotePriority.Medium);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("انخفاض الشهية", result.Value.Title);
        Assert.Equal(NoteCategory.Nutrition, result.Value.Category);

        // Verify activity log was created
        var activity = await db.ElderlyActivityLogs.SingleOrDefaultAsync(l => l.ElderlyId == elderly.Id);
        Assert.NotNull(activity);
        Assert.Contains("إضافة ملاحظة", activity!.Summary);
    }

    [Fact]
    public async Task GetActivityTimeline_ReturnsCorrectMetrics()
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

        // Add 3 sample activity logs
        db.ElderlyActivityLogs.Add(ElderlyActivityLog.Create(
            elderly.Id, userId, ElderlyActivityType.ViewMedicalProfile, "عرض السجل"));
        db.ElderlyActivityLogs.Add(ElderlyActivityLog.Create(
            elderly.Id, userId, ElderlyActivityType.UpdateMedications, "تحديث أدوية"));

        await db.SaveChangesAsync();

        var handler = new GetElderlyActivityTimelineQueryHandler(db);
        var query = new GetElderlyActivityTimelineQuery(userId, elderly.Id);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalEventsCount);
        Assert.Equal(2, result.Value.ThisWeekEventsCount);
        Assert.Equal(1, result.Value.UniqueUsersCount);
        Assert.Equal(2, result.Value.Activities.Count);
    }
}