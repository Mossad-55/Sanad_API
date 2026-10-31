using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Application.HelpRequests;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Domain.HelpRequests;
using Ahlena.Modules.Families.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Families.HelpRequests;

public sealed class AcknowledgeFamilyDependentHelpRequestTests
{
    [Fact]
    public async Task Handle_OwnerAcknowledgesPendingRequest_RecordsAcknowledgement()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var setup = AddFixture(db, owner);
        var result = await new AcknowledgeFamilyDependentHelpRequestCommandHandler(db)
            .Handle(new(owner, setup.Elderly.Id, setup.Request.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(ElderlyHelpRequestStatus.Acknowledged, result.Value.Status);
        Assert.Contains(db.ElderlyHelpRequestHistories,
            history => history.Action == ElderlyHelpRequestHistoryAction.Acknowledged);
    }

    [Fact]
    public async Task Handle_DifferentFamilyMemberCannotAcknowledgeRequest()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var setup = AddFixture(db, owner);
        var result = await new AcknowledgeFamilyDependentHelpRequestCommandHandler(db)
            .Handle(new(UserId.New(), setup.Elderly.Id, setup.Request.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.Elderly.FamilyNotFound", result.Error.Code);
        Assert.Equal(ElderlyHelpRequestStatus.Pending, setup.Request.Status);
    }

    [Fact]
    public async Task Handle_NonPendingRequestReturnsConflict()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var setup = AddFixture(db, owner);
        setup.Request.Transition(ElderlyHelpRequestHistoryAction.Accepted, null, owner);
        await db.SaveChangesAsync();

        var result = await new AcknowledgeFamilyDependentHelpRequestCommandHandler(db)
            .Handle(new(owner, setup.Elderly.Id, setup.Request.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.HelpRequest.InvalidOperation", result.Error.Code);
    }

    private static (Family Family, Elderly Elderly, ElderlyHelpRequest Request) AddFixture(
        FamiliesDbContext db,
        UserId owner)
    {
        var family = Family.Create(owner, "Test family");
        var elderly = Elderly.Create(owner, UserId.New(), family.Id, FamilyRelationshipType.Father,
            FullName.Create("Test"), FullName.Create("Test"), Gender.Male,
            new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        var request = ElderlyHelpRequest.Create(elderly.IdentityUserId, family.Id.Value,
            "actor", "actor", "actor", "action", "action", "action",
            "need", "need", "need", null, null, null, null, DateTime.UtcNow.ToString());
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.ElderlyHelpRequests.Add(request);
        db.SaveChanges();
        return (family, elderly, request);
    }

    private static FamiliesDbContext CreateDb() => new(
        new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
