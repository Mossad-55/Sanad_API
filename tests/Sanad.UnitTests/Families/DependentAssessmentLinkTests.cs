using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Application.Elderlies;
using Sanad.Modules.Families.Domain.Assessments;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class DependentAssessmentLinkTests
{
    private static FamiliesDbContext CreateDbContext(string? databaseName = null) =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task AssessmentLink_ShouldRejectSecondConcurrentLink()
    {
        string databaseName = Guid.NewGuid().ToString();
        using FamiliesDbContext first = CreateDbContext(databaseName);
        using FamiliesDbContext second = CreateDbContext(databaseName);
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Family");
        AssessmentTier tier = CreateTier();
        CareAssessment assessment = CreateAssessment(family, tier, null, DateTime.UtcNow);
        first.Families.Add(family);
        first.AssessmentTiers.Add(tier);
        first.CareAssessments.Add(assessment);
        await first.SaveChangesAsync();

        CareAssessment firstCopy = await first.CareAssessments.SingleAsync();
        CareAssessment secondCopy = await second.CareAssessments.SingleAsync();
        firstCopy.LinkToElderly(ElderlyId.New());
        secondCopy.LinkToElderly(ElderlyId.New());

        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => second.SaveChangesAsync());
    }

    [Fact]
    public async Task AddDependent_ShouldLinkSameFamilyUnlinkedAssessment_AndKeepOlderRows()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Family");
        AssessmentTier tier = CreateTier();
        CareAssessment previous = CreateAssessment(family, tier, null, DateTime.UtcNow.AddDays(-1));
        CareAssessment target = CreateAssessment(family, tier, null, DateTime.UtcNow);
        db.Families.Add(family);
        db.AssessmentTiers.Add(tier);
        db.CareAssessments.AddRange(previous, target);
        await db.SaveChangesAsync();
        var gateway = new FakeIdentityGateway();

        Result<DependentResponse> result = await CreateHandler(db, gateway).Handle(
            NewCommand(owner, target.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(result.Value.Id, target.ElderlyId);
        Assert.Collection(
            await db.CareAssessments.ToListAsync(),
            _ => { },
            _ => { });
        Assert.Null(previous.ElderlyId);
        Assert.Single(await db.Elderlies.ToListAsync());
        Assert.Empty(gateway.DeletedUsers);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("linked")]
    public async Task AddDependent_ShouldRejectInvalidAssessment_AndCompensateIdentityCreation(string kind)
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Family");
        Family otherFamily = Family.Create(UserId.New(), "Other family");
        AssessmentTier tier = CreateTier();
        db.Families.AddRange(family, otherFamily);
        db.AssessmentTiers.Add(tier);
        ElderlyId? existingElderlyId = kind == "linked" ? ElderlyId.New() : null;
        CareAssessment? assessment = kind switch
        {
            "foreign" => CreateAssessment(otherFamily, tier, null, DateTime.UtcNow),
            "linked" => CreateAssessment(
                family,
                tier,
                existingElderlyId,
                DateTime.UtcNow),
            _ => null
        };
        if (assessment is not null)
        {
            db.CareAssessments.Add(assessment);
        }
        await db.SaveChangesAsync();
        var gateway = new FakeIdentityGateway();
        CareAssessmentId requestedId = assessment?.Id ?? CareAssessmentId.New();

        Result<DependentResponse> result = await CreateHandler(db, gateway).Handle(
            NewCommand(owner, requestedId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.Assessment.InvalidSubmission", result.Error.Code);
        Assert.Empty(await db.Elderlies.ToListAsync());
        Assert.Single(gateway.CreatedUsers);
        Assert.Equal(gateway.CreatedUsers, gateway.DeletedUsers);
        Assert.Equal(existingElderlyId, assessment?.ElderlyId);
    }

    [Fact]
    public async Task GetDependent_ShouldReturnNullLatestAssessment_WhenNoAssessmentsExist()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Family");
        AssessmentTier tier = CreateTier();
        var elderly = Elderly.Create(
            owner,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("الاسم"),
            FullName.Create("Dependent"),
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(DateTime.UtcNow));
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.AssessmentTiers.Add(tier);
        await db.SaveChangesAsync();
        var handler = new GetDependentQueryHandler(db, new FakeIdentityGateway());

        Result<DependentResponse> withoutAssessments = await handler.Handle(
            new GetDependentQuery(owner, elderly.Id), CancellationToken.None);

        Assert.True(withoutAssessments.IsSuccess);
        Assert.Null(withoutAssessments.Value.LatestAssessment);
    }

    [Fact]
    public async Task GetDependent_ShouldReturnLatestLinkedAssessment()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Family");
        AssessmentTier tier = CreateTier();
        var elderly = Elderly.Create(
            owner,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("Elderly"),
            FullName.Create("Elderly"),
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(DateTime.UtcNow));
        CareAssessment older = CreateAssessment(
            family,
            tier,
            elderly.Id,
            DateTime.UtcNow.AddDays(-1));
        CareAssessment latest = CreateAssessment(
            family,
            tier,
            elderly.Id,
            DateTime.UtcNow);
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.AssessmentTiers.Add(tier);
        db.CareAssessments.AddRange(older, latest);
        await db.SaveChangesAsync();

        var handler = new GetDependentQueryHandler(db, new FakeIdentityGateway());
        Result<DependentResponse> result = await handler.Handle(
            new GetDependentQuery(owner, elderly.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.LatestAssessment);
        Assert.Equal(latest.Id, result.Value.LatestAssessment.AssessmentId);
        Assert.Equal(2, await db.CareAssessments.CountAsync());
    }

    private static AddDependentCommandHandler CreateHandler(
        FamiliesDbContext db,
        FakeIdentityGateway gateway) => new(db, gateway);

    private static AddDependentCommand NewCommand(UserId owner, CareAssessmentId assessmentId) =>
        new(
            owner,
            "الاسم",
            "Dependent",
            "+201000000000",
            Gender.Male,
            FamilyRelationshipType.Grandfather,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            null,
            null,
            null,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTime.UtcNow,
            assessmentId);

    private static AssessmentTier CreateTier() => AssessmentTier.Create(
        1, "T", "T", "D", "D", "#fff", "B", "B", "i.png", 0, 10, ["r"], ["r"]);

    private static CareAssessment CreateAssessment(
        Family family,
        AssessmentTier tier,
        ElderlyId? elderlyId,
        DateTime completedOnUtc) =>
        CareAssessment.Create(
            family.Id,
            elderlyId,
            tier.Id,
            1,
            [(AssessmentQuestionId.New(), AssessmentOptionId.New(), 1)],
            completedOnUtc);

    private sealed class FakeIdentityGateway : IFamilyIdentityGateway
    {
        public List<UserId> CreatedUsers { get; } = [];
        public List<UserId> DeletedUsers { get; } = [];

        public Task<Result<ElderlyIdentityAccount>> GetElderlyByPhoneAsync(
            string phoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ElderlyIdentityAccount>.Failure(
                new Error("Identity.Elderly.NotFound", "Not found.")));

        public Task<Result<ElderlyIdentityAccount>> CreateElderlyAsync(
            string arabicFullName,
            string englishFullName,
            string phoneNumber,
            Gender gender,
            DateOnly dateOfBirth,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            UserId userId = UserId.New();
            CreatedUsers.Add(userId);
            return Task.FromResult(Result<ElderlyIdentityAccount>.Success(
                new ElderlyIdentityAccount(userId, Exists: true, IsElderly: true)));
        }

        public Task DeleteElderlyAsync(UserId userId, CancellationToken cancellationToken = default)
        {
            DeletedUsers.Add(userId);
            return Task.CompletedTask;
        }

        public Task<Result<FamilyInviteeAccount>> GetFamilyInviteeByEmailAsync(
            string email,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<IReadOnlyList<FamilyMemberProfile>> GetFamilyMemberProfilesAsync(
            IReadOnlyCollection<UserId> userIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FamilyMemberProfile>>([]);

        public Task SendFamilyInvitationEmailAsync(
            string email,
            string familyName,
            string invitationToken,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Result> AnonymizeFamilyAccountsAsync(
            IReadOnlyCollection<UserId> userIds,
            CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
    }
}
