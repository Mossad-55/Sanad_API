using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class FamilyLeaveTests
{
    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }

    // ------------------------------ Domain ------------------------------

    [Fact]
    public void TransferOwnership_PromotesTarget_DemotesOldOwner_AndBumpsUpdatedOnUtc()
    {
        UserId owner = UserId.New();
        UserId target = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            target,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Viewer));

        DateTime before = family.UpdatedOnUtc;

        family.TransferOwnership(target);

        Assert.Equal(FamilyRole.Owner, family.GetRole(target));
        Assert.Equal(FamilyRole.Editor, family.GetRole(owner));
        Assert.Equal(target, family.OwnerUserId);
        Assert.True(family.UpdatedOnUtc >= before);
    }

    [Fact]
    public void TransferOwnership_Throws_WhenTargetIsNotAMember()
    {
        UserId owner = UserId.New();
        UserId target = UserId.New();
        Family family = Family.Create(owner);

        Assert.Throws<DomainException>(
            () => family.TransferOwnership(target));
    }

    // ----------------------------- Handler ------------------------------

    [Fact]
    public async Task LeaveFamily_ReturnsOwnerProtected_WhenOwnerLeavesWithoutTransfer()
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

        var handler = new LeaveFamilyCommandHandler(db);

        Result result =
            await handler.Handle(
                new LeaveFamilyCommand(owner, null),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.OwnerProtected, result.Error);
        Assert.Contains(family.Members, member => member.Id == owner);
        Assert.Contains(family.Members, member => member.Id == memberId);
    }

    [Fact]
    public async Task LeaveFamily_ReturnsMemberNotFound_WhenTransferTargetIsUnknown()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId unknownMember = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new LeaveFamilyCommandHandler(db);

        Result result =
            await handler.Handle(
                new LeaveFamilyCommand(owner, unknownMember),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.MemberNotFound, result.Error);
    }

    [Fact]
    public async Task LeaveFamily_RemovesCaller_AndPromotesTarget_WhenOwnerTransfers()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId target = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            target,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Viewer));

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new LeaveFamilyCommandHandler(db);

        Result result =
            await handler.Handle(
                new LeaveFamilyCommand(owner, target),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(target, family.OwnerUserId);
        Assert.DoesNotContain(family.Members, member => member.Id == owner);
        Assert.Equal(FamilyRole.Owner, family.GetRole(target));
    }

    [Fact]
    public async Task LeaveFamily_RemovesCaller_WhenViewerLeaves()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId viewer = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");
        family.AddMember(FamilyMember.Create(
            viewer,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Viewer));

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new LeaveFamilyCommandHandler(db);

        Result result =
            await handler.Handle(
                new LeaveFamilyCommand(viewer, UserId.New()),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(owner, family.OwnerUserId);
        Assert.DoesNotContain(family.Members, member => member.Id == viewer);
    }
}
