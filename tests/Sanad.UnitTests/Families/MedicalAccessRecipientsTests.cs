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

public sealed class MedicalAccessRecipientsTests
{
    [Fact]
    public async Task List_ShouldReturnEligibleCandidatesWithGrantFlags()
    {
        await using var db = CreateDb();
        var fixture = AddFixture(db);
        db.MedicalAccessGrants.Add(MedicalAccessGrant.Create(
            fixture.Elderly.Id, fixture.Owner, fixture.FirstGrantee, MedicalAccessGrantType.Limited,
            true, false, false, DateTime.UtcNow));
        var expiredAt = DateTime.UtcNow.AddMinutes(-1);
        db.MedicalAccessGrants.Add(MedicalAccessGrant.Create(
            fixture.Elderly.Id, fixture.Owner, fixture.SecondGrantee, MedicalAccessGrantType.Limited,
            true, false, false, expiredAt.AddMinutes(-5), expiredAt));
        await db.SaveChangesAsync();

        var result = await Handler(db, fixture.Candidates).Handle(
            new GetMedicalAccessRecipientsQuery(fixture.Elderly.Id.Value, fixture.Owner),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(1, result.Value.TotalPages);
        var first = Assert.Single(result.Value.Items, i => i.UserId == fixture.FirstGrantee.Value);
        var second = Assert.Single(result.Value.Items, i => i.UserId == fixture.SecondGrantee.Value);
        Assert.True(first.HasActiveGrant);
        Assert.False(second.HasActiveGrant);
        Assert.Equal("Medical", first.CaregiverType);
        Assert.Equal(fixture.FirstCaregiverId, first.CaregiverId);
    }

    [Fact]
    public async Task List_ShouldDenyForeignDependent()
    {
        await using var db = CreateDb();
        var fixture = AddFixture(db);
        var otherFamily = Family.Create(UserId.New(), "Other family");
        var other = Elderly.Create(
            UserId.New(), UserId.New(), otherFamily.Id, FamilyRelationshipType.Father,
            FullName.Create("Other"), FullName.Create("Other"), Gender.Male,
            new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        db.Families.Add(otherFamily);
        db.Elderlies.Add(other);
        await db.SaveChangesAsync();

        var result = await Handler(db, fixture.Candidates).Handle(
            new GetMedicalAccessRecipientsQuery(other.Id.Value, fixture.Owner),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.AccessDenied", result.Error.Code);
    }

    [Fact]
    public async Task List_ShouldDenyNonMember()
    {
        await using var db = CreateDb();
        var fixture = AddFixture(db);

        var result = await Handler(db, fixture.Candidates).Handle(
            new GetMedicalAccessRecipientsQuery(fixture.Elderly.Id.Value, UserId.New()),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.AccessDenied", result.Error.Code);
    }

    [Fact]
    public async Task List_ShouldApplySearchAndPagination()
    {
        await using var db = CreateDb();
        var fixture = AddFixture(db);

        var searched = await Handler(db, fixture.Candidates).Handle(
            new GetMedicalAccessRecipientsQuery(
                fixture.Elderly.Id.Value, fixture.Owner, "Kareem", 1, 20),
            default);

        Assert.True(searched.IsSuccess);
        var match = Assert.Single(searched.Value.Items);
        Assert.Equal(fixture.SecondGrantee.Value, match.UserId);
        Assert.Equal(1, searched.Value.TotalCount);

        var paged = await Handler(db, fixture.Candidates).Handle(
            new GetMedicalAccessRecipientsQuery(
                fixture.Elderly.Id.Value, fixture.Owner, null, 2, 1),
            default);

        Assert.True(paged.IsSuccess);
        Assert.Equal(2, paged.Value.Page);
        Assert.Equal(1, paged.Value.PageSize);
        Assert.Equal(2, paged.Value.TotalCount);
        Assert.Equal(2, paged.Value.TotalPages);
        Assert.Single(paged.Value.Items);
    }

    private static GetMedicalAccessRecipientsQueryHandler Handler(
        FamiliesDbContext db,
        IReadOnlyList<MedicalCaregiverRecipient> candidates) =>
        new(db, new FakeRecipientGateway(candidates));

    private static Fixture AddFixture(FamiliesDbContext db)
    {
        var owner = UserId.New();
        var firstGrantee = UserId.New();
        var secondGrantee = UserId.New();
        var firstCaregiverId = Guid.CreateVersion7();
        var secondCaregiverId = Guid.CreateVersion7();
        var family = Family.Create(owner, "Recipient tests");
        var elderly = Elderly.Create(owner, UserId.New(), family.Id, FamilyRelationshipType.Father,
            FullName.Create("Test"), FullName.Create("Test"), Gender.Male,
            new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.SaveChanges();

        var candidates = new List<MedicalCaregiverRecipient>
        {
            new(firstCaregiverId, firstGrantee.Value, "أحمد", "Ahmed", null,
                Guid.CreateVersion7(), "تمريض", "Nursing"),
            new(secondCaregiverId, secondGrantee.Value, "كريم", "Kareem", "https://cdn.example/a.png",
                null, null, null)
        };

        return new(owner, firstGrantee, secondGrantee, firstCaregiverId, elderly, candidates);
    }

    private static FamiliesDbContext CreateDb() => new(
        new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed record Fixture(
        UserId Owner,
        UserId FirstGrantee,
        UserId SecondGrantee,
        Guid FirstCaregiverId,
        Elderly Elderly,
        IReadOnlyList<MedicalCaregiverRecipient> Candidates);

    private sealed class FakeRecipientGateway(
        IReadOnlyList<MedicalCaregiverRecipient> candidates) : IMedicalCaregiverGateway
    {
        public Task<bool> IsActiveMedicalCaregiverAsync(UserId userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<MedicalCaregiverRecipientPage> SearchActiveMedicalCaregiversAsync(
            string? search, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var filtered = string.IsNullOrWhiteSpace(search)
                ? candidates
                : candidates.Where(c =>
                    c.ArabicFullName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    c.EnglishFullName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

            var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(new MedicalCaregiverRecipientPage(items, filtered.Count));
        }
    }
}
