using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Identity;
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
    private sealed class FakeIdentityGateway : IFamilyIdentityGateway
    {
        public Task<IReadOnlyList<FamilyMemberProfile>> GetFamilyMemberProfilesAsync(
            IReadOnlyCollection<UserId> userIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FamilyMemberProfile>>([]);

        public Task<Result<ElderlyIdentityAccount>> GetElderlyByPhoneAsync(
            string phoneNumber,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Result<ElderlyIdentityAccount>> CreateElderlyAsync(
            string arabicFullName,
            string englishFullName,
            string phoneNumber,
            Gender gender,
            DateOnly dateOfBirth,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task DeleteElderlyAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Result<FamilyInviteeAccount>> GetFamilyInviteeByEmailAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task SendFamilyInvitationEmailAsync(
            string email,
            string familyName,
            string invitationToken,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Result> AnonymizeFamilyAccountsAsync(
            IReadOnlyCollection<UserId> userIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

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

        var handler = new GetElderlyActivityTimelineQueryHandler(db, new FakeIdentityGateway());
        var query = new GetElderlyActivityTimelineQuery(userId, elderly.Id);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalEventsCount);
        Assert.Equal(2, result.Value.ThisWeekEventsCount);
        Assert.Equal(1, result.Value.UniqueUsersCount);
        Assert.Equal(2, result.Value.Activities.Count);
    }

    [Fact]
    public async Task SameFamilyEditorCanAddUpdateAndDeleteNote()
    {
        using var db = CreateDbContext();
        var owner = UserId.New();
        var editor = UserId.New();
        var family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            editor,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Editor));
        var elderly = Elderly.Create(
            owner,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Father,
            FullName.Create("Ø£Ø­Ù…Ø¯"),
            FullName.Create("Ahmed"),
            Gender.Male,
            new DateOnly(1955, 1, 1),
            DateOnly.FromDateTime(DateTime.UtcNow));

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        await db.SaveChangesAsync();

        var addResult = await new AddElderlyNoteCommandHandler(db).Handle(
            new AddElderlyNoteCommand(
                editor,
                elderly.Id,
                "Editor note",
                "Initial description",
                NoteCategory.General,
                NotePriority.Low),
            CancellationToken.None);

        Assert.True(addResult.IsSuccess);
        var noteId = (await db.ElderlyNotes.SingleAsync()).Id;

        var updateResult = await new UpdateElderlyNoteCommandHandler(db).Handle(
            new UpdateElderlyNoteCommand(
                editor,
                elderly.Id,
                noteId,
                "Updated editor note",
                "Updated description",
                NoteCategory.HealthSymptoms,
                NotePriority.High),
            CancellationToken.None);

        Assert.True(updateResult.IsSuccess);
        Assert.Equal("Updated editor note", updateResult.Value.Title);
        Assert.Equal(NoteCategory.HealthSymptoms, updateResult.Value.Category);

        var deleteResult = await new DeleteElderlyNoteCommandHandler(db).Handle(
            new DeleteElderlyNoteCommand(editor, elderly.Id, noteId),
            CancellationToken.None);

        Assert.True(deleteResult.IsSuccess);
        Assert.Empty(await db.ElderlyNotes.ToListAsync());
    }

    [Fact]
    public async Task SameFamilyViewerCanReadNotes()
    {
        using var db = CreateDbContext();
        var owner = UserId.New();
        var viewer = UserId.New();
        var family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            viewer,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Viewer));
        var elderly = Elderly.Create(
            owner,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Father,
            FullName.Create("Ø£Ø­Ù…Ø¯"),
            FullName.Create("Ahmed"),
            Gender.Male,
            new DateOnly(1955, 1, 1),
            DateOnly.FromDateTime(DateTime.UtcNow));
        var note = ElderlyNote.Create(
            elderly.Id,
            owner,
            "Visible note",
            "Visible description",
            NoteCategory.General,
            NotePriority.Medium);

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.ElderlyNotes.Add(note);
        await db.SaveChangesAsync();

        var result = await new ListElderlyNotesQueryHandler(db).Handle(
            new ListElderlyNotesQuery(viewer, elderly.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("Visible note", result.Value[0].Title);
    }

    [Fact]
    public async Task SameFamilyViewerCannotAddUpdateOrDeleteNote()
    {
        using var db = CreateDbContext();
        var owner = UserId.New();
        var viewer = UserId.New();
        var family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            viewer,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Viewer));
        var elderly = Elderly.Create(
            owner,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Father,
            FullName.Create("Ø£Ø­Ù…Ø¯"),
            FullName.Create("Ahmed"),
            Gender.Male,
            new DateOnly(1955, 1, 1),
            DateOnly.FromDateTime(DateTime.UtcNow));
        var note = ElderlyNote.Create(
            elderly.Id,
            owner,
            "Owner note",
            "Owner description",
            NoteCategory.General,
            NotePriority.Medium);

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.ElderlyNotes.Add(note);
        await db.SaveChangesAsync();

        var addResult = await new AddElderlyNoteCommandHandler(db).Handle(
            new AddElderlyNoteCommand(
                viewer,
                elderly.Id,
                "Viewer add",
                "Should be denied",
                NoteCategory.General,
                NotePriority.Low),
            CancellationToken.None);
        Assert.True(addResult.IsFailure);
        Assert.Equal(NoteErrors.AccessDenied, addResult.Error);

        var updateResult = await new UpdateElderlyNoteCommandHandler(db).Handle(
            new UpdateElderlyNoteCommand(
                viewer,
                elderly.Id,
                note.Id,
                "Viewer update",
                "Should be denied",
                NoteCategory.General,
                NotePriority.High),
            CancellationToken.None);
        Assert.True(updateResult.IsFailure);
        Assert.Equal(NoteErrors.AccessDenied, updateResult.Error);

        var deleteResult = await new DeleteElderlyNoteCommandHandler(db).Handle(
            new DeleteElderlyNoteCommand(viewer, elderly.Id, note.Id),
            CancellationToken.None);
        Assert.True(deleteResult.IsFailure);
        Assert.Equal(NoteErrors.AccessDenied, deleteResult.Error);

        Assert.Equal("Owner note", (await db.ElderlyNotes.SingleAsync()).Title);
    }

    [Theory]
    [InlineData(FamilyRole.Owner)]
    [InlineData(FamilyRole.Editor)]
    public async Task ForeignFamilyOwnerOrEditorCannotReadOrMutateKnownNote(
        FamilyRole callerRole)
    {
        using var db = CreateDbContext();
        var caller = UserId.New();
        var callerFamilyOwner = callerRole == FamilyRole.Owner ? caller : UserId.New();
        var targetFamilyOwner = UserId.New();
        var callerFamily = Family.Create(callerFamilyOwner, "Caller Family");
        if (callerRole == FamilyRole.Editor)
        {
            callerFamily.AddMember(FamilyMember.Create(
                caller,
                callerFamilyOwner,
                FamilyRelationshipType.Other,
                FamilyRole.Editor));
        }

        var targetFamily = Family.Create(targetFamilyOwner, "Target Family");
        var elderly = Elderly.Create(
            targetFamilyOwner,
            UserId.New(),
            targetFamily.Id,
            FamilyRelationshipType.Father,
            FullName.Create("Ø£Ø­Ù…Ø¯"),
            FullName.Create("Ahmed"),
            Gender.Male,
            new DateOnly(1955, 1, 1),
            DateOnly.FromDateTime(DateTime.UtcNow));
        var note = ElderlyNote.Create(
            elderly.Id,
            targetFamilyOwner,
            "Private note",
            "Private description",
            NoteCategory.General,
            NotePriority.Medium);

        db.Families.AddRange(callerFamily, targetFamily);
        db.Elderlies.Add(elderly);
        db.ElderlyNotes.Add(note);
        await db.SaveChangesAsync();

        var listResult = await new ListElderlyNotesQueryHandler(db).Handle(
            new ListElderlyNotesQuery(caller, elderly.Id),
            CancellationToken.None);
        Assert.True(listResult.IsSuccess);
        Assert.Empty(listResult.Value);

        var updateResult = await new UpdateElderlyNoteCommandHandler(db).Handle(
            new UpdateElderlyNoteCommand(
                caller,
                elderly.Id,
                note.Id,
                "Attempted update",
                "Attempted description",
                NoteCategory.General,
                NotePriority.High),
            CancellationToken.None);
        Assert.True(updateResult.IsFailure);
        Assert.Equal(NoteErrors.NotFound, updateResult.Error);

        var deleteResult = await new DeleteElderlyNoteCommandHandler(db).Handle(
            new DeleteElderlyNoteCommand(caller, elderly.Id, note.Id),
            CancellationToken.None);
        Assert.True(deleteResult.IsFailure);
        Assert.Equal(NoteErrors.NotFound, deleteResult.Error);

        var addResult = await new AddElderlyNoteCommandHandler(db).Handle(
            new AddElderlyNoteCommand(
                caller,
                elderly.Id,
                "Attempted add",
                "Attempted description",
                NoteCategory.General,
                NotePriority.Low),
            CancellationToken.None);
        Assert.True(addResult.IsFailure);
        Assert.Equal(NoteErrors.DependentNotFound, addResult.Error);

        Assert.Equal(1, await db.ElderlyNotes.CountAsync());
        Assert.Equal("Private note", (await db.ElderlyNotes.SingleAsync()).Title);
    }
}
