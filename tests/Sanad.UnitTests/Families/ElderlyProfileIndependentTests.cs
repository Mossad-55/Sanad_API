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
    public async Task EmergencyContact_FamilyMembersCanRead_AndOnlyCurrentOwnerCanWrite()
    {
        await using FamiliesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId member = UserId.New();
        Family family = Family.Create(owner);
        family.AddMember(FamilyMember.Create(member, owner, FamilyRelationshipType.Other, FamilyRole.Viewer));
        Elderly elderly = CreateElderly(owner, UserId.New(), family.Id, ElderlyTimeZone.InitialDefaultId, DateOnly.Parse("1980-01-01"));
        db.AddRange(family, elderly);
        await db.SaveChangesAsync();

        Result<EmergencyContactResponse> written = await new SetFamilyEmergencyContactCommandHandler(db)
            .Handle(new SetFamilyEmergencyContactCommand(owner, elderly.Id, "Amina", "Daughter", "+201000000000"), CancellationToken.None);
        Assert.True(written.IsSuccess);

        Result<EmergencyContactResponse?> visible = await new GetFamilyEmergencyContactQueryHandler(db)
            .Handle(new GetFamilyEmergencyContactQuery(member, elderly.Id), CancellationToken.None);
        Assert.True(visible.IsSuccess);
        Assert.Equal("Amina", visible.Value!.Name);
        Assert.Equal("+201000000000", visible.Value.PhoneNumber);

        Result<EmergencyContactResponse> denied = await new SetFamilyEmergencyContactCommandHandler(db)
            .Handle(new SetFamilyEmergencyContactCommand(member, elderly.Id, "Other", "Friend", "+201111111111"), CancellationToken.None);
        Assert.True(denied.IsFailure);
        Assert.Equal("Families.Elderly.AccessDenied", denied.Error.Code);
    }

    [Theory]
    [InlineData(FamilyRole.Editor)]
    [InlineData(FamilyRole.Viewer)]
    public async Task EmergencyContact_EveryLinkedMemberRoleCanRead(FamilyRole role)
    {
        await using FamiliesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId member = UserId.New();
        Family family = Family.Create(owner);
        family.AddMember(FamilyMember.Create(member, owner, FamilyRelationshipType.Other, role));
        Elderly elderly = CreateElderly(owner, UserId.New(), family.Id, ElderlyTimeZone.InitialDefaultId, DateOnly.Parse("1980-01-01"));
        elderly.SetEmergencyContact("Amina", "Daughter", "+201000000000");
        db.AddRange(family, elderly);
        await db.SaveChangesAsync();

        Result<EmergencyContactResponse?> result = await new GetFamilyEmergencyContactQueryHandler(db)
            .Handle(new GetFamilyEmergencyContactQuery(member, elderly.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Amina", result.Value!.Name);
    }

    [Theory]
    [InlineData(FamilyRole.Editor)]
    [InlineData(FamilyRole.Viewer)]
    public async Task EmergencyContact_NonOwnerCannotWriteRegardlessOfMemberRole(FamilyRole role)
    {
        await using FamiliesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId member = UserId.New();
        Family family = Family.Create(owner);
        family.AddMember(FamilyMember.Create(member, owner, FamilyRelationshipType.Other, role));
        Elderly elderly = CreateElderly(owner, UserId.New(), family.Id, ElderlyTimeZone.InitialDefaultId, DateOnly.Parse("1980-01-01"));
        elderly.SetEmergencyContact("Amina", "Daughter", "+201000000000");
        db.AddRange(family, elderly);
        await db.SaveChangesAsync();

        Result<EmergencyContactResponse> result = await new SetFamilyEmergencyContactCommandHandler(db)
            .Handle(new SetFamilyEmergencyContactCommand(member, elderly.Id, "Other", "Friend", "+201111111111"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.Elderly.AccessDenied", result.Error.Code);
        Assert.Equal("Amina", (await db.Elderlies.SingleAsync()).EmergencyContactName);
    }

    [Fact]
    public async Task EmergencyContact_OwnerCanUpdateAndInvalidPhoneLeavesSavedContactUnchanged()
    {
        await using FamiliesDbContext db = CreateDb();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        Elderly elderly = CreateElderly(owner, UserId.New(), family.Id, ElderlyTimeZone.InitialDefaultId, DateOnly.Parse("1980-01-01"));
        db.AddRange(family, elderly);
        await db.SaveChangesAsync();
        var handler = new SetFamilyEmergencyContactCommandHandler(db);

        Result<EmergencyContactResponse> updated = await handler.Handle(
            new SetFamilyEmergencyContactCommand(owner, elderly.Id, "  Amina  ", "  Daughter ", "+201000000000"), CancellationToken.None);
        Assert.True(updated.IsSuccess);
        Assert.Equal("Amina", updated.Value.Name);

        Result<EmergencyContactResponse> invalid = await handler.Handle(
            new SetFamilyEmergencyContactCommand(owner, elderly.Id, "Other", "Friend", "01000000000"), CancellationToken.None);

        Assert.True(invalid.IsFailure);
        Assert.Equal("Families.Elderly.InvalidProfile", invalid.Error.Code);
        Elderly persisted = await db.Elderlies.SingleAsync();
        Assert.Equal("Amina", persisted.EmergencyContactName);
        Assert.Equal("+201000000000", persisted.EmergencyContactPhoneNumber);
    }

    [Fact]
    public async Task EmergencyContact_ReadAndWriteCannotCrossFamilyBoundary()
    {
        await using FamiliesDbContext db = CreateDb();
        UserId caller = UserId.New();
        UserId otherOwner = UserId.New();
        Family callerFamily = Family.Create(caller);
        Family otherFamily = Family.Create(otherOwner);
        Elderly elderly = CreateElderly(otherOwner, UserId.New(), otherFamily.Id, ElderlyTimeZone.InitialDefaultId, DateOnly.Parse("1980-01-01"));
        elderly.SetEmergencyContact("Amina", "Daughter", "+201000000000");
        db.AddRange(callerFamily, otherFamily, elderly);
        await db.SaveChangesAsync();

        Result<EmergencyContactResponse?> read = await new GetFamilyEmergencyContactQueryHandler(db)
            .Handle(new GetFamilyEmergencyContactQuery(caller, elderly.Id), CancellationToken.None);
        Result<EmergencyContactResponse> write = await new SetFamilyEmergencyContactCommandHandler(db)
            .Handle(new SetFamilyEmergencyContactCommand(caller, elderly.Id, "Other", "Friend", "+201111111111"), CancellationToken.None);

        Assert.True(read.IsFailure);
        Assert.Equal("Families.Elderly.NotFound", read.Error.Code);
        Assert.True(write.IsFailure);
        Assert.Equal("Families.Elderly.NotFound", write.Error.Code);
        Assert.Equal("Amina", (await db.Elderlies.SingleAsync()).EmergencyContactName);
    }

    [Fact]
    public async Task EmergencyContact_DeletedFamilyCannotReadOrWrite()
    {
        await using FamiliesDbContext db = CreateDb();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        Elderly elderly = CreateElderly(owner, UserId.New(), family.Id, ElderlyTimeZone.InitialDefaultId, DateOnly.Parse("1980-01-01"));
        elderly.SetEmergencyContact("Amina", "Daughter", "+201000000000");
        family.MarkDeleted("owner request", null);
        db.AddRange(family, elderly);
        await db.SaveChangesAsync();

        Result<EmergencyContactResponse?> read = await new GetFamilyEmergencyContactQueryHandler(db)
            .Handle(new GetFamilyEmergencyContactQuery(owner, elderly.Id), CancellationToken.None);
        Result<EmergencyContactResponse> write = await new SetFamilyEmergencyContactCommandHandler(db)
            .Handle(new SetFamilyEmergencyContactCommand(owner, elderly.Id, "Other", "Friend", "+201111111111"), CancellationToken.None);

        Assert.True(read.IsFailure);
        Assert.Equal("Families.Elderly.FamilyNotFound", read.Error.Code);
        Assert.True(write.IsFailure);
        Assert.Equal("Families.Elderly.AccessDenied", write.Error.Code);
        Assert.Equal("Amina", (await db.Elderlies.SingleAsync()).EmergencyContactName);
    }

    [Theory]
    [InlineData("01000000000")]
    [InlineData("+0123456789")]
    [InlineData("+1234567890123456")]
    public async Task EmergencyContact_ValidatorRejectsInvalidPhoneNumber(string phoneNumber)
    {
        var validator = new SetFamilyEmergencyContactCommandValidator();
        var command = new SetFamilyEmergencyContactCommand(UserId.New(), ElderlyId.New(), "Amina", "Daughter", phoneNumber);

        FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.PhoneNumber));
    }

    [Fact]
    public async Task OwnProfile_IncludesContactOnlyForIdentityLinkedElderly()
    {
        await using FamiliesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId identity = UserId.New();
        Family family = Family.Create(owner);
        Elderly elderly = CreateElderly(owner, identity, family.Id, ElderlyTimeZone.InitialDefaultId, DateOnly.Parse("1980-01-01"));
        elderly.SetEmergencyContact("Amina", "Daughter", "+201000000000");
        db.AddRange(family, elderly);
        await db.SaveChangesAsync();

        Result<ElderlyOwnProfileResponse> own = await new GetOwnElderlyProfileQueryHandler(db)
            .Handle(new GetOwnElderlyProfileQuery(identity), CancellationToken.None);
        Result<ElderlyOwnProfileResponse> familyOwner = await new GetOwnElderlyProfileQueryHandler(db)
            .Handle(new GetOwnElderlyProfileQuery(owner), CancellationToken.None);
        Result<ElderlyOwnProfileResponse> another = await new GetOwnElderlyProfileQueryHandler(db)
            .Handle(new GetOwnElderlyProfileQuery(UserId.New()), CancellationToken.None);

        Assert.True(own.IsSuccess);
        Assert.Equal("Amina", own.Value.EmergencyContact!.Name);
        Assert.True(familyOwner.IsFailure);
        Assert.True(another.IsFailure);
    }

    [Fact]
    public async Task OwnProfile_DoesNotExposeContactAfterFamilyDeletion()
    {
        await using FamiliesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId identity = UserId.New();
        Family family = Family.Create(owner);
        Elderly elderly = CreateElderly(owner, identity, family.Id, ElderlyTimeZone.InitialDefaultId, DateOnly.Parse("1980-01-01"));
        elderly.SetEmergencyContact("Amina", "Daughter", "+201000000000");
        family.MarkDeleted("owner request", null);
        db.AddRange(family, elderly);
        await db.SaveChangesAsync();

        Result<ElderlyOwnProfileResponse> result = await new GetOwnElderlyProfileQueryHandler(db)
            .Handle(new GetOwnElderlyProfileQuery(identity), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.Elderly.NotFound", result.Error.Code);
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
