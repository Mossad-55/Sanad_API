using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Application.Activities;
using Sanad.Modules.Families.Application.Elderlies;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Activities;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class ElderlyActivityHandlerTests
{
    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }

    private sealed class FakeIdentityGateway : IFamilyIdentityGateway
    {
        private readonly IReadOnlyList<FamilyMemberProfile> _profiles;

        public FakeIdentityGateway(
            params FamilyMemberProfile[] profiles)
        {
            _profiles = profiles;
        }

        public Task<IReadOnlyList<FamilyMemberProfile>>
            GetFamilyMemberProfilesAsync(
                IReadOnlyCollection<UserId> userIds,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FamilyMemberProfile>>(
                _profiles
                    .Where(profile =>
                        userIds.Contains(profile.UserId))
                    .ToList());

        public Task<Result<ElderlyIdentityAccount>>
            GetElderlyByPhoneAsync(
                string phoneNumber,
                CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Result<ElderlyIdentityAccount>>
            CreateElderlyAsync(
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
            throw new NotImplementedException();

        public Task<Result<FamilyInviteeAccount>>
            GetFamilyInviteeByEmailAsync(
                string email,
                CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task SendFamilyInvitationEmailAsync(
            string email,
            string familyName,
            string invitationToken,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Result> AnonymizeFamilyAccountsAsync(
            IReadOnlyCollection<UserId> userIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

    [Fact]
    public async Task Handle_EnrichesActorNames_WhenProfilesExist()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId actorId = UserId.New();
        ElderlyId elderlyId = ElderlyId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            actorId,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Editor));

        db.Families.Add(family);
        db.Elderlies.Add(Elderly.Create(
            owner,
            actorId,
            family.Id,
            FamilyRelationshipType.Other,
            FullName.Create("أحمد"),
            FullName.Create("Ahmed"),
            Gender.Male,
            new DateOnly(1950, 1, 1),
            new DateOnly(2026, 9, 2)));
        await db.SaveChangesAsync();

        var log = ElderlyActivityLog.Create(
            elderlyId,
            actorId,
            ElderlyActivityType.ViewMedicalProfile,
            "Viewed the medical profile.");
        db.ElderlyActivityLogs.Add(log);
        await db.SaveChangesAsync();

        var gateway = new FakeIdentityGateway(
            new FamilyMemberProfile(
                actorId,
                "أحمد محمد النصر",
                "Ahmed Mohamed El-Nasr",
                "ahmed@example.com"));
        var handler = new GetElderlyActivityTimelineQueryHandler(db, gateway);

        Result<ElderlyActivityDashboardResponse> result =
            await handler.Handle(
                new GetElderlyActivityTimelineQuery(owner, elderlyId),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Activities);
        Assert.Equal("أحمد محمد النصر", result.Value.Activities[0].ArabicFullName);
        Assert.Equal("Ahmed Mohamed El-Nasr", result.Value.Activities[0].EnglishFullName);
    }

    [Fact]
    public async Task Handle_ItemsSurviveWithEmptyNames_WhenProfileMissing()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId actorId = UserId.New();
        ElderlyId elderlyId = ElderlyId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            actorId,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Editor));

        db.Families.Add(family);
        db.Elderlies.Add(Elderly.Create(
            owner,
            actorId,
            family.Id,
            FamilyRelationshipType.Other,
            FullName.Create("أحمد"),
            FullName.Create("Ahmed"),
            Gender.Male,
            new DateOnly(1950, 1, 1),
            new DateOnly(2026, 9, 2)));
        await db.SaveChangesAsync();

        var log = ElderlyActivityLog.Create(
            elderlyId,
            actorId,
            ElderlyActivityType.AddNote,
            "Test note");
        db.ElderlyActivityLogs.Add(log);
        await db.SaveChangesAsync();

        var gateway = new FakeIdentityGateway();
        var handler = new GetElderlyActivityTimelineQueryHandler(db, gateway);

        Result<ElderlyActivityDashboardResponse> result =
            await handler.Handle(
                new GetElderlyActivityTimelineQuery(owner, elderlyId),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Activities);
        Assert.Equal(string.Empty, result.Value.Activities[0].ArabicFullName);
        Assert.Equal(string.Empty, result.Value.Activities[0].EnglishFullName);
    }

    [Fact]
    public async Task Handle_ClampsLimit()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        ElderlyId elderlyId = ElderlyId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");

        db.Families.Add(family);
        db.Elderlies.Add(Elderly.Create(
            owner,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Other,
            FullName.Create("أحمد"),
            FullName.Create("Ahmed"),
            Gender.Male,
            new DateOnly(1950, 1, 1),
            new DateOnly(2026, 9, 2)));
        await db.SaveChangesAsync();

        for (int i = 0; i < 10; i++)
        {
            db.ElderlyActivityLogs.Add(ElderlyActivityLog.Create(
                elderlyId,
                owner,
                ElderlyActivityType.AddNote,
                $"Note {i}"));
        }
        await db.SaveChangesAsync();

        var gateway = new FakeIdentityGateway();
        var handler = new GetElderlyActivityTimelineQueryHandler(db, gateway);

        Result<ElderlyActivityDashboardResponse> result =
            await handler.Handle(
                new GetElderlyActivityTimelineQuery(owner, elderlyId, Limit: 5),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.Activities.Count);
    }

    [Fact]
    public async Task Handle_ViewerCannotAccessFeed()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId viewerId = UserId.New();
        ElderlyId elderlyId = ElderlyId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            viewerId,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Viewer));

        db.Families.Add(family);
        db.Elderlies.Add(Elderly.Create(
            owner,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Other,
            FullName.Create("أحمد"),
            FullName.Create("Ahmed"),
            Gender.Male,
            new DateOnly(1950, 1, 1),
            new DateOnly(2026, 9, 2)));
        await db.SaveChangesAsync();

        var gateway = new FakeIdentityGateway();
        var handler = new GetElderlyActivityTimelineQueryHandler(db, gateway);

        Result<ElderlyActivityDashboardResponse> result =
            await handler.Handle(
                new GetElderlyActivityTimelineQuery(viewerId, elderlyId),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.AccessDenied, result.Error);
    }

    [Fact]
    public async Task GetMedicalProfile_WritesOneViewEvent_WithCallerUserId()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        ElderlyId elderlyId = ElderlyId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");

        db.Families.Add(family);
        var elderly = Elderly.Create(
            owner,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Other,
            FullName.Create("أحمد"),
            FullName.Create("Ahmed"),
            Gender.Male,
            new DateOnly(1950, 1, 1),
            new DateOnly(2026, 9, 2),
            healthNotes: "Test health notes");
        db.Elderlies.Add(elderly);
        await db.SaveChangesAsync();

        var handler = new GetElderlyMedicalProfileQueryHandler(db);
        var query = new GetElderlyMedicalProfileQuery(owner, elderly.Id);

        Result<ElderlyMedicalProfileResponse> result =
            await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var logs = await db.ElderlyActivityLogs
            .AsNoTracking()
            .ToListAsync(CancellationToken.None);
        Assert.Single(logs);
        Assert.Equal(ElderlyActivityType.ViewMedicalProfile, logs[0].ActivityType);
        Assert.Equal(owner, logs[0].ActorUserId);
    }

    [Fact]
    public async Task GetMedicalProfile_WriteSkipped_OnNotFound()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        ElderlyId elderlyId = ElderlyId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new GetElderlyMedicalProfileQueryHandler(db);
        var query = new GetElderlyMedicalProfileQuery(owner, elderlyId);

        Result<ElderlyMedicalProfileResponse> result =
            await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ElderlyErrors.NotFound, result.Error);

        var logs = await db.ElderlyActivityLogs
            .AsNoTracking()
            .ToListAsync(CancellationToken.None);
        Assert.Empty(logs);
    }
}
