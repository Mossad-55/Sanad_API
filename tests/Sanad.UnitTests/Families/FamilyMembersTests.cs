using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class FamilyMembersTests
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
    }

    // ------------------------------ Domain ------------------------------

    [Fact]
    public void ChangeMemberRole_UpdatesRole_AndBumpsUpdatedOnUtc()
    {
        UserId owner = UserId.New();
        UserId memberId = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            memberId,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Viewer));

        DateTime before = family.UpdatedOnUtc;

        family.ChangeMemberRole(memberId, FamilyRole.Editor);

        Assert.Equal(FamilyRole.Editor, family.GetRole(memberId));
        Assert.True(family.UpdatedOnUtc >= before);
    }

    [Fact]
    public void ChangeMemberRole_Throws_WhenTargetIsOwner()
    {
        UserId owner = UserId.New();
        Family family = Family.Create(owner);

        Assert.Throws<DomainException>(
            () => family.ChangeMemberRole(
                owner,
                FamilyRole.Editor));
    }

    [Fact]
    public void ChangeMemberRole_Throws_WhenAssigningOwnerRole()
    {
        UserId owner = UserId.New();
        UserId memberId = UserId.New();
        Family family = Family.Create(owner);
        family.AddMember(FamilyMember.Create(
            memberId,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Viewer));

        Assert.Throws<DomainException>(
            () => family.ChangeMemberRole(
                memberId,
                FamilyRole.Owner));
    }

    [Fact]
    public void ChangeMemberRole_Throws_WhenMemberUnknown()
    {
        UserId owner = UserId.New();
        Family family = Family.Create(owner);

        Assert.Throws<DomainException>(
            () => family.ChangeMemberRole(
                UserId.New(),
                FamilyRole.Editor));
    }

    [Fact]
    public void RemoveMember_Throws_WhenRemovingOwner()
    {
        UserId owner = UserId.New();
        Family family = Family.Create(owner);

        Assert.Throws<DomainException>(
            () => family.RemoveMember(owner));
    }

    // ----------------------------- Get member ---------------------------

    [Fact]
    public async Task GetFamilyMember_ReturnsEnrichedMember_ForMember()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId memberId = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            memberId,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Editor));

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var gateway = new FakeIdentityGateway(
            new FamilyMemberProfile(
                memberId,
                "أحمد محمد النصر",
                "Ahmed Mohamed El-Nasr",
                "ahmed@example.com"));
        var handler = new GetFamilyMemberQueryHandler(db, gateway);

        Result<FamilyMemberResponse> result =
            await handler.Handle(
                new GetFamilyMemberQuery(owner, memberId),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(memberId, result.Value.UserId);
        Assert.Equal(FamilyRole.Editor, result.Value.Role);
        Assert.Equal("أحمد محمد النصر", result.Value.ArabicFullName);
        Assert.Equal("Ahmed Mohamed El-Nasr", result.Value.EnglishFullName);
        Assert.Equal("ahmed@example.com", result.Value.Email);
    }

    [Fact]
    public async Task GetFamilyMember_ReturnsNotFound_WhenNoFamily()
    {
        using FamiliesDbContext db = CreateDbContext();
        var gateway = new FakeIdentityGateway();
        var handler = new GetFamilyMemberQueryHandler(db, gateway);

        Result<FamilyMemberResponse> result =
            await handler.Handle(
                new GetFamilyMemberQuery(UserId.New(), UserId.New()),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task GetFamilyMember_ReturnsMemberNotFound_WhenMemberUnknown()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var gateway = new FakeIdentityGateway();
        var handler = new GetFamilyMemberQueryHandler(db, gateway);

        Result<FamilyMemberResponse> result =
            await handler.Handle(
                new GetFamilyMemberQuery(owner, UserId.New()),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.MemberNotFound, result.Error);
    }

    [Fact]
    public async Task GetFamilyMember_ReturnsEmptyEnrichment_WhenProfileMissing()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId memberId = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            memberId,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Viewer));

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var gateway = new FakeIdentityGateway();
        var handler = new GetFamilyMemberQueryHandler(db, gateway);

        Result<FamilyMemberResponse> result =
            await handler.Handle(
                new GetFamilyMemberQuery(owner, memberId),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Value.ArabicFullName);
        Assert.Equal(string.Empty, result.Value.EnglishFullName);
        Assert.Null(result.Value.Email);
    }

    // ----------------------------- Change role --------------------------

    [Fact]
    public async Task ChangeFamilyMemberRole_Succeeds_ForOwner()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId memberId = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            memberId,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Viewer));

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new ChangeFamilyMemberRoleCommandHandler(db);

        Result result =
            await handler.Handle(
                new ChangeFamilyMemberRoleCommand(
                    owner,
                    memberId,
                    FamilyRole.Editor),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FamilyRole.Editor, family.GetRole(memberId));
    }

    [Fact]
    public async Task ChangeFamilyMemberRole_ReturnsNotOwner_ForNonOwner()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId editorId = UserId.New();
        UserId memberId = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            editorId,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Editor));
        family.AddMember(FamilyMember.Create(
            memberId,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Viewer));

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new ChangeFamilyMemberRoleCommandHandler(db);

        Result result =
            await handler.Handle(
                new ChangeFamilyMemberRoleCommand(
                    editorId,
                    memberId,
                    FamilyRole.Editor),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.NotOwner, result.Error);
    }

    [Fact]
    public async Task ChangeFamilyMemberRole_ReturnsNotFound_WhenNoFamily()
    {
        using FamiliesDbContext db = CreateDbContext();
        var handler = new ChangeFamilyMemberRoleCommandHandler(db);

        Result result =
            await handler.Handle(
                new ChangeFamilyMemberRoleCommand(
                    UserId.New(),
                    UserId.New(),
                    FamilyRole.Editor),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task ChangeFamilyMemberRole_ReturnsMemberNotFound_WhenMemberUnknown()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new ChangeFamilyMemberRoleCommandHandler(db);

        Result result =
            await handler.Handle(
                new ChangeFamilyMemberRoleCommand(
                    owner,
                    UserId.New(),
                    FamilyRole.Editor),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.MemberNotFound, result.Error);
    }

    [Fact]
    public async Task ChangeFamilyMemberRole_ReturnsOwnerProtected_WhenTargetOwner()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new ChangeFamilyMemberRoleCommandHandler(db);

        Result result =
            await handler.Handle(
                new ChangeFamilyMemberRoleCommand(
                    owner,
                    owner,
                    FamilyRole.Editor),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.OwnerProtected, result.Error);
    }

    // ------------------------------- Remove -----------------------------

    [Fact]
    public async Task RemoveFamilyMember_Succeeds_ForOwner()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId memberId = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            memberId,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Viewer));

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new RemoveFamilyMemberCommandHandler(db);

        Result result =
            await handler.Handle(
                new RemoveFamilyMemberCommand(owner, memberId),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(family.GetRole(memberId));
    }

    [Fact]
    public async Task RemoveFamilyMember_ReturnsNotOwner_ForNonOwner()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId editorId = UserId.New();
        UserId memberId = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            editorId,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Editor));
        family.AddMember(FamilyMember.Create(
            memberId,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Viewer));

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new RemoveFamilyMemberCommandHandler(db);

        Result result =
            await handler.Handle(
                new RemoveFamilyMemberCommand(editorId, memberId),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.NotOwner, result.Error);
    }

    [Fact]
    public async Task RemoveFamilyMember_ReturnsNotFound_WhenNoFamily()
    {
        using FamiliesDbContext db = CreateDbContext();
        var handler = new RemoveFamilyMemberCommandHandler(db);

        Result result =
            await handler.Handle(
                new RemoveFamilyMemberCommand(UserId.New(), UserId.New()),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task RemoveFamilyMember_ReturnsMemberNotFound_WhenMemberUnknown()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new RemoveFamilyMemberCommandHandler(db);

        Result result =
            await handler.Handle(
                new RemoveFamilyMemberCommand(owner, UserId.New()),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.MemberNotFound, result.Error);
    }

    [Fact]
    public async Task RemoveFamilyMember_ReturnsOwnerProtected_WhenTargetOwner()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new RemoveFamilyMemberCommandHandler(db);

        Result result =
            await handler.Handle(
                new RemoveFamilyMemberCommand(owner, owner),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.OwnerProtected, result.Error);
    }

    // --------------------- Family list enrichment -----------------------

    [Fact]
    public async Task GetMyFamily_EnrichesMembers_WhenProfilesExist()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId memberId = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            memberId,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Editor));

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var gateway = new FakeIdentityGateway(
            new FamilyMemberProfile(
                owner,
                "أحمد محمد النصر",
                "Ahmed Mohamed El-Nasr",
                "ahmed@example.com"),
            new FamilyMemberProfile(
                memberId,
                "سلمى نصر",
                "Salma Nasr",
                "salma@example.com"));
        var handler = new GetMyFamilyQueryHandler(db, gateway);

        Result<FamilyResponse> result =
            await handler.Handle(
                new GetMyFamilyQuery(owner),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        FamilyMemberResponse ownerMember =
            result.Value.Members.Single(m => m.UserId == owner);
        Assert.Equal("أحمد محمد النصر", ownerMember.ArabicFullName);
        Assert.Equal("Ahmed Mohamed El-Nasr", ownerMember.EnglishFullName);
        Assert.Equal("ahmed@example.com", ownerMember.Email);

        FamilyMemberResponse invitedMember =
            result.Value.Members.Single(m => m.UserId == memberId);
        Assert.Equal("سلمى نصر", invitedMember.ArabicFullName);
        Assert.Equal("Salma Nasr", invitedMember.EnglishFullName);
        Assert.Equal("salma@example.com", invitedMember.Email);
    }
}
