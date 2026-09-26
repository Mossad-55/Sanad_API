using System.Text;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Elderlies;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Domain.Assessments;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class ElderlyProfileIndependentTests
{
    [Fact]
    public async Task OwnProfile_ResolvesByIdentity_UsesTimezoneAgeAndLatestAssessment()
    {
        await using FamiliesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId identity = UserId.New();
        Family family = Family.Create(owner, "Family");
        Elderly elderly = CreateElderly(owner, identity, family.Id, "Asia/Riyadh", DateOnly.Parse("1980-01-01"));
        db.AddRange(family, elderly);
        await db.SaveChangesAsync();

        Result<ElderlyOwnProfileResponse> result = await new GetOwnElderlyProfileQueryHandler(db)
            .Handle(new GetOwnElderlyProfileQuery(identity), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(elderly.Id, result.Value.Id);
        Assert.Equal("Asia/Riyadh", result.Value.TimeZoneId);
        Assert.Null(result.Value.LatestAssessment);
        Assert.Equal("/api/v1/elderly/profile/photo", result.Value.PhotoUrl);
        Assert.True(result.Value.Age is 45 or 46);
    }

    [Fact]
    public async Task OwnProfilePhoto_RequiresIdentityOwnership_AndReadsPrivateStorageKey()
    {
        await using FamiliesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId identity = UserId.New();
        Family family = Family.Create(owner);
        Elderly elderly = CreateElderly(owner, identity, family.Id, ElderlyTimeZone.InitialDefaultId,
            DateOnly.Parse("1980-01-01"), "elderly-photos/private.png");
        db.AddRange(family, elderly);
        await db.SaveChangesAsync();
        var storage = new FakeFileStorage();

        Result<DependentPhotoContent> denied = await new GetOwnElderlyProfilePhotoQueryHandler(db, storage)
            .Handle(new GetOwnElderlyProfilePhotoQuery(UserId.New()), CancellationToken.None);
        Assert.True(denied.IsFailure);
        Assert.Empty(storage.OpenedKeys);

        Result<DependentPhotoContent> result = await new GetOwnElderlyProfilePhotoQueryHandler(db, storage)
            .Handle(new GetOwnElderlyProfilePhotoQuery(identity), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal("elderly-photos/private.png", storage.OpenedKeys.Single());
        Assert.Equal($"elderly-{elderly.Id.Value:N}.png", result.Value.FileName);
        Assert.Equal("image/png", result.Value.ContentType);
    }

    [Fact]
    public async Task ChangeTimezone_AllowsOwner_AndPersistsNormalizedIanaId()
    {
        await using FamiliesDbContext db = CreateDb();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        Elderly elderly = CreateElderly(owner, UserId.New(), family.Id, ElderlyTimeZone.InitialDefaultId,
            DateOnly.Parse("1980-01-01"));
        db.AddRange(family, elderly);
        await db.SaveChangesAsync();

        Result<DependentTimeZoneResponse> result = await new ChangeDependentTimeZoneCommandHandler(db)
            .Handle(new ChangeDependentTimeZoneCommand(owner, elderly.Id, "  Asia/Tokyo "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Asia/Tokyo", result.Value.TimeZoneId);
        Assert.Equal("Asia/Tokyo", (await db.Elderlies.SingleAsync()).TimeZoneId);
    }

    [Theory]
    [InlineData(FamilyRole.Editor)]
    [InlineData(FamilyRole.Viewer)]
    public async Task ChangeTimezone_DeniesNonOwners(FamilyRole role)
    {
        await using FamiliesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId member = UserId.New();
        Family family = Family.Create(owner);
        family.AddMember(FamilyMember.Create(member, owner, FamilyRelationshipType.Other, role));
        Elderly elderly = CreateElderly(owner, UserId.New(), family.Id, ElderlyTimeZone.InitialDefaultId,
            DateOnly.Parse("1980-01-01"));
        db.AddRange(family, elderly);
        await db.SaveChangesAsync();

        Result<DependentTimeZoneResponse> result = await new ChangeDependentTimeZoneCommandHandler(db)
            .Handle(new ChangeDependentTimeZoneCommand(member, elderly.Id, "Asia/Tokyo"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.Elderly.AccessDenied", result.Error.Code);
        Assert.Equal(ElderlyTimeZone.InitialDefaultId, (await db.Elderlies.SingleAsync()).TimeZoneId);
    }

    [Fact]
    public async Task ChangeTimezone_RejectsInvalidIanaIdWithoutMutation()
    {
        await using FamiliesDbContext db = CreateDb();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        Elderly elderly = CreateElderly(owner, UserId.New(), family.Id, ElderlyTimeZone.InitialDefaultId,
            DateOnly.Parse("1980-01-01"));
        db.AddRange(family, elderly);
        await db.SaveChangesAsync();

        Result<DependentTimeZoneResponse> result = await new ChangeDependentTimeZoneCommandHandler(db)
            .Handle(new ChangeDependentTimeZoneCommand(owner, elderly.Id, "Mars/Olympus"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.Elderly.InvalidProfile", result.Error.Code);
        Assert.Equal(ElderlyTimeZone.InitialDefaultId, (await db.Elderlies.SingleAsync()).TimeZoneId);
    }

    [Fact]
    public async Task AddDependent_UsesInitialDefaultTimezoneWhenNoOverrideIsProvided()
    {
        await using FamiliesDbContext db = CreateDb();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        db.Families.Add(family);
        await db.SaveChangesAsync();

        Result<DependentResponse> result = await new AddDependentCommandHandler(db, new NewElderlyGateway())
            .Handle(new AddDependentCommand(owner, "الاسم", "Name", "+201000000000", Gender.Male,
                FamilyRelationshipType.Father, DateOnly.Parse("1980-01-01"), null, null, null,
                DateOnly.Parse("2026-09-26"), DateTime.UtcNow), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ElderlyTimeZone.InitialDefaultId, result.Value.TimeZoneId);
    }

    private static FamiliesDbContext CreateDb() => new(new DbContextOptionsBuilder<FamiliesDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Elderly CreateElderly(UserId owner, UserId identity, FamilyId familyId, string timeZone,
        DateOnly dob, string? photo = null)
    {
        Elderly elderly = Elderly.Create(owner, identity, familyId, FamilyRelationshipType.Father,
            FullName.Create("الاسم"), FullName.Create("Name"), Gender.Male, dob,
            DateOnly.Parse("2026-09-26"), photo);
        if (timeZone != ElderlyTimeZone.InitialDefaultId)
            elderly.ChangeTimeZone(timeZone);
        return elderly;
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        public List<string> OpenedKeys { get; } = [];
        public Task<Result<StoredFile>> SaveAsync(Stream c, string t, long l, string f, CancellationToken x = default) => throw new NotSupportedException();
        public Task<Result<StoredFile>> SavePrivateAsync(Stream c, string t, long l, string f, CancellationToken x = default) => throw new NotSupportedException();
        public Task<Result> DeleteAsync(string k, CancellationToken x = default) => throw new NotSupportedException();
        public Task<Result<PrivateFileContent>> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        {
            OpenedKeys.Add(key);
            return Task.FromResult<Result<PrivateFileContent>>(new PrivateFileContent(key, "image/png",
                new MemoryStream(Encoding.UTF8.GetBytes("private"))));
        }
    }

    private sealed class NewElderlyGateway : IFamilyIdentityGateway
    {
        public Task<Result<ElderlyIdentityAccount>> GetElderlyByPhoneAsync(string p, CancellationToken c = default) =>
            Task.FromResult(Result<ElderlyIdentityAccount>.Failure(new Error("Identity.Elderly.NotFound", "missing")));
        public Task<Result<ElderlyIdentityAccount>> CreateElderlyAsync(string a, string e, string p, Gender g, DateOnly d, DateTime u, CancellationToken c = default) =>
            Task.FromResult(Result<ElderlyIdentityAccount>.Success(new ElderlyIdentityAccount(UserId.New(), true, true)));
        public Task DeleteElderlyAsync(UserId u, CancellationToken c = default) => Task.CompletedTask;
        public Task<Result<FamilyInviteeAccount>> GetFamilyInviteeByEmailAsync(string e, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<FamilyMemberProfile>> GetFamilyMemberProfilesAsync(IReadOnlyCollection<UserId> ids, CancellationToken c = default) => Task.FromResult<IReadOnlyList<FamilyMemberProfile>>([]);
        public Task SendFamilyInvitationEmailAsync(string e, string f, string t, CancellationToken c = default) => Task.CompletedTask;
        public Task<Result> AnonymizeFamilyAccountsAsync(IReadOnlyCollection<UserId> ids, CancellationToken c = default) => Task.FromResult(Result.Success());
    }
}
