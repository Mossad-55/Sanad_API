using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
using Sanad.Modules.Families.Application.MedicalAccess;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class MedicalAccessGrantHandlerTests
{
    [Fact]
    public async Task Create_RejectsDuplicateActiveGrantForSameDependentAndGrantee()
    {
        await using var db = CreateDb();
        var fixture = AddFixture(db);
        db.MedicalAccessGrants.Add(MedicalAccessGrant.Create(
            fixture.Elderly.Id, fixture.Owner, fixture.Grantee, MedicalAccessGrantType.Limited,
            true, false, false, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(Command(fixture), default);

        Assert.True(result.IsFailure);
        Assert.Equal("MedicalAccess.GrantExists", result.Error.Code);
    }

    [Fact]
    public async Task Create_AllowsReissueWhenPreviousGrantHasExpired()
    {
        await using var db = CreateDb();
        var fixture = AddFixture(db);
        var expiredAt = DateTime.UtcNow.AddMinutes(-1);
        db.MedicalAccessGrants.Add(MedicalAccessGrant.Create(
            fixture.Elderly.Id, fixture.Owner, fixture.Grantee, MedicalAccessGrantType.Limited,
            true, false, false, expiredAt.AddMinutes(-5), expiredAt));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(Command(fixture), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, await db.MedicalAccessGrants.CountAsync());
    }

    [Fact]
    public async Task Create_RejectsNonMedicalCaregiverRecipient()
    {
        await using var db = CreateDb();
        var fixture = AddFixture(db);
        var result = await new CreateMedicalAccessGrantCommandHandler(db, new FakeGateway(false))
            .Handle(Command(fixture), default);

        Assert.True(result.IsFailure);
        Assert.Equal("MedicalAccess.InvalidGrant", result.Error.Code);
        Assert.Empty(db.MedicalAccessGrants);
    }

    private static CreateMedicalAccessGrantCommand Command(Fixture fixture) => new(
        fixture.Elderly.Id, fixture.Owner, fixture.Grantee, MedicalAccessGrantType.Limited,
        true, false, false, DateTime.UtcNow.AddDays(30), "test");

    private static CreateMedicalAccessGrantCommandHandler Handler(FamiliesDbContext db) =>
        new(db, new FakeGateway(true));

    private static Fixture AddFixture(FamiliesDbContext db)
    {
        var owner = UserId.New();
        var grantee = UserId.New();
        var family = Family.Create(owner, "Grant tests");
        var elderly = Elderly.Create(owner, UserId.New(), family.Id, FamilyRelationshipType.Father,
            FullName.Create("Test"), FullName.Create("Test"), Gender.Male,
            new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.SaveChanges();
        return new(owner, grantee, elderly);
    }

    private static FamiliesDbContext CreateDb() => new(
        new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed record Fixture(UserId Owner, UserId Grantee, Elderly Elderly);

    private sealed class FakeGateway(bool active) : IMedicalCaregiverGateway
    {
        public Task<bool> IsActiveMedicalCaregiverAsync(UserId userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(active);
    }
}
