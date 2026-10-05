using MediatR;
using Microsoft.EntityFrameworkCore;
using Sanad.API.CareHomesIntegration;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.CareHomes.Application.FamilyIntake;
using Sanad.Modules.Families.Application.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies.Medical;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.CareHomes;

public sealed class ElderlyIntakeResolverTests
{
    private static readonly DateOnly ReferenceDate = new(2026, 10, 3);

    [Fact]
    public async Task ResolveAsync_AllowsOwnerAndEditor_AndProjectsServerDataAndContactRules()
    {
        await using var db = CreateDb();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        Family family = Family.Create(owner, "Test family");
        family.AddMember(FamilyMember.Create(editor, owner, FamilyRelationshipType.Sister, FamilyRole.Editor));
        Elderly elderly = Elderly.Create(owner, UserId.New(), family.Id, FamilyRelationshipType.Mother,
            FullName.Create("Ù…ØµØ±ÙŠ"), FullName.Create("Elderly server name"), Gender.Female,
            new DateOnly(1950, 10, 4), ReferenceDate);
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        await db.SaveChangesAsync();

        var sender = new IntakeSender(
            new MyProfileResponse("Ø§Ù„Ù…Ø±Ø³Ù„", "Submitting member", null, "+201000000000", AccountType.Family, true, true, null),
            new ElderlyMedicalProfileResponse(elderly.Id, BloodType.APositive, 165, 70.5m,
                ["Diabetes"], [new AllergyDto(AllergyCategory.Food, "Peanut", "Rash")],
                [new MedicalHistoryDto(2020, "Surgery", "Recovered", new DateOnly(2020, 2, 3))],
                new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc)));
        var resolver = new FamilyElderlyIntakeResolver(db, sender);

        Result<ElderlyIntakeResolution> ownerResult = await resolver.ResolveAsync(Request(owner, family.Id, elderly.Id));
        Result<ElderlyIntakeResolution> editorResult = await resolver.ResolveAsync(Request(editor, family.Id, elderly.Id,
            name: "  Explicit contact ", phone: " +201111111111 ", relationship: " Daughter "));

        Assert.True(ownerResult.IsSuccess);
        Assert.Equal(75, ownerResult.Value.Age);
        Assert.Equal("Elderly server name", ownerResult.Value.EnglishFullName);
        Assert.Equal("Submitting member", ownerResult.Value.ResponsibleContact.Name);
        Assert.Equal("+201000000000", ownerResult.Value.ResponsibleContact.PhoneNumber);
        Assert.Null(ownerResult.Value.ResponsibleContact.Relationship);
        Assert.Equal("Diabetes", ownerResult.Value.MedicalProfile.ChronicConditions.Single());
        Assert.Equal("Peanut", ownerResult.Value.MedicalProfile.Allergies.Single().Allergen);
        Assert.Equal("Explicit contact", editorResult.Value.ResponsibleContact.Name);
        Assert.Equal("+201111111111", editorResult.Value.ResponsibleContact.PhoneNumber);
        Assert.Equal("Daughter", editorResult.Value.ResponsibleContact.Relationship);
        Assert.Equal(2, sender.MedicalRequests);
    }

    [Fact]
    public async Task ResolveAsync_DeniesViewerAndOutsider_AndConvergesNotFoundCases()
    {
        await using var db = CreateDb();
        UserId owner = UserId.New();
        UserId viewer = UserId.New();
        UserId outsider = UserId.New();
        Family family = Family.Create(owner);
        family.AddMember(FamilyMember.Create(viewer, owner, FamilyRelationshipType.Sister, FamilyRole.Viewer));
        Elderly elderly = AddElderly(db, family, owner);
        Family otherFamily = Family.Create(outsider);
        Elderly otherElderly = AddElderly(db, otherFamily, outsider);
        Family deletedFamily = Family.Create(owner);
        deletedFamily.MarkDeleted("removed", null);
        db.Families.AddRange(family, otherFamily, deletedFamily);
        await db.SaveChangesAsync();
        var sender = new IntakeSender(DefaultProfile, DefaultMedical);
        var resolver = new FamilyElderlyIntakeResolver(db, sender);

        Result<ElderlyIntakeResolution> viewerResult = await resolver.ResolveAsync(Request(viewer, family.Id, elderly.Id));
        Result<ElderlyIntakeResolution> outsiderResult = await resolver.ResolveAsync(Request(outsider, family.Id, elderly.Id));
        Result<ElderlyIntakeResolution> crossFamilyResult = await resolver.ResolveAsync(Request(owner, family.Id, otherElderly.Id));
        Result<ElderlyIntakeResolution> missingDependentResult = await resolver.ResolveAsync(Request(owner, family.Id, ElderlyId.New()));
        Result<ElderlyIntakeResolution> missingFamilyResult = await resolver.ResolveAsync(Request(owner, FamilyId.New(), elderly.Id));
        Result<ElderlyIntakeResolution> deletedResult = await resolver.ResolveAsync(Request(owner, deletedFamily.Id, elderly.Id));

        Assert.All(new[] { viewerResult, outsiderResult, crossFamilyResult, missingDependentResult, missingFamilyResult, deletedResult }, result =>
        {
            Assert.True(result.IsFailure);
            Assert.Equal("CareHomes.FamilyIntake.NotFound", result.Error.Code);
        });
        Assert.Equal(0, sender.MedicalRequests);
    }

    [Fact]
    public async Task ResolveAsync_UsesMedicalQueryUnknownProjectionWhenProfileIsMissing()
    {
        await using var db = CreateDb();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        Elderly elderly = AddElderly(db, family, owner);
        db.Families.Add(family);
        await db.SaveChangesAsync();
        var resolver = new FamilyElderlyIntakeResolver(db, new IntakeSender(DefaultProfile,
            new ElderlyMedicalProfileResponse(elderly.Id, BloodType.Unknown, null, null, [], [], [], null)));

        Result<ElderlyIntakeResolution> result = await resolver.ResolveAsync(Request(owner, family.Id, elderly.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal("Unknown", result.Value.MedicalProfile.BloodType);
        Assert.Null(result.Value.MedicalProfile.HeightCm);
        Assert.Empty(result.Value.MedicalProfile.MedicalHistory);
    }

    [Fact]
    public async Task ResolveAsync_ConvergesFailedMedicalLookupToNotFound()
    {
        await using var db = CreateDb();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        Elderly elderly = AddElderly(db, family, owner);
        db.Families.Add(family);
        await db.SaveChangesAsync();
        var resolver = new FamilyElderlyIntakeResolver(db,
            new IntakeSender(DefaultProfile, DefaultMedical, failMedical: true));

        Result<ElderlyIntakeResolution> result = await resolver.ResolveAsync(Request(owner, family.Id, elderly.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("CareHomes.FamilyIntake.NotFound", result.Error.Code);
    }

    [Theory]
    [InlineData(1950, 10, 3, 76)]
    [InlineData(1950, 10, 4, 75)]
    [InlineData(1950, 10, 5, 75)]
    public async Task ResolveAsync_CalculatesAgeAtEgyptReferenceDateBoundary(int year, int month, int day, int expectedAge)
    {
        await using var db = CreateDb();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        Elderly elderly = Elderly.Create(owner, UserId.New(), family.Id, FamilyRelationshipType.Mother,
            FullName.Create("Ù…ØµØ±ÙŠ"), FullName.Create("Elderly"), Gender.Female,
            new DateOnly(year, month, day), ReferenceDate);
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        await db.SaveChangesAsync();

        Result<ElderlyIntakeResolution> result = await new FamilyElderlyIntakeResolver(db,
            new IntakeSender(DefaultProfile, DefaultMedical)).ResolveAsync(Request(owner, family.Id, elderly.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedAge, result.Value.Age);
    }

    [Fact]
    public async Task ResolveAsync_RejectsEmptyIdsDateInvalidPhoneAndLengthBoundaries()
    {
        await using var db = CreateDb();
        var resolver = new FamilyElderlyIntakeResolver(db, new IntakeSender(DefaultProfile, DefaultMedical));
        ElderlyIntakeRequest valid = Request(UserId.New(), FamilyId.New(), ElderlyId.New());

        var invalidRequests = new[]
        {
            valid with { SubmittedByUserId = UserId.Empty },
            valid with { FamilyId = FamilyId.Empty },
            valid with { ElderlyId = ElderlyId.Empty },
            valid with { EgyptLocalReferenceDate = default },
            valid with { ResponsibleContactPhone = "01000000000" },
            valid with { ResponsibleContactName = new string('x', Elderly.MaximumEmergencyContactNameLength + 1) },
            valid with { ResponsibleContactRelationship = new string('x', Elderly.MaximumEmergencyContactRelationshipLength + 1) },
            valid with { CareNeedsNotes = new string('x', Elderly.MaximumHealthNotesLength + 1) }
        };

        foreach (ElderlyIntakeRequest request in invalidRequests)
        {
            Result<ElderlyIntakeResolution> result = await resolver.ResolveAsync(request);
            Assert.True(result.IsFailure);
            Assert.Equal("CareHomes.FamilyIntake.Invalid", result.Error.Code);
        }
    }

    [Fact]
    public void IntakeContract_KeepsServerOwnedElderlyProjectionAndCallerRelationshipSeparate()
    {
        var resolution = new ElderlyIntakeResolution(
            FamilyId.New(),
            ElderlyId.New(),
            UserId.New(),
            "مسن",
            "Elderly",
            75,
            new ElderlyIntakeMedicalProjection(
                "Unknown", null, null, [], [], [], null),
            new ElderlyIntakeResponsibleContact(
                "Submitting member", "+201000000000", "Daughter"),
            "Needs assistance");

        Assert.Equal("Elderly", resolution.EnglishFullName);
        Assert.Equal(75, resolution.Age);
        Assert.Equal("Daughter", resolution.ResponsibleContact.Relationship);
        Assert.Empty(resolution.MedicalProfile.ChronicConditions);
    }

    [Fact]
    public void IntakeRequest_UsesExplicitEgyptLocalReferenceDate()
    {
        var request = new ElderlyIntakeRequest(
            UserId.New(),
            FamilyId.New(),
            ElderlyId.New(),
            new DateOnly(2026, 10, 3));

        Assert.Equal(new DateOnly(2026, 10, 3), request.EgyptLocalReferenceDate);
    }

    private static readonly MyProfileResponse DefaultProfile =
        new("Ø§Ù„Ù…Ø±Ø³Ù„", "Submitting member", null, "+201000000000", AccountType.Family, true, true, null);

    private static readonly ElderlyMedicalProfileResponse DefaultMedical =
        new(ElderlyId.New(), BloodType.Unknown, null, null, [], [], [], null);

    private static FamiliesDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Elderly AddElderly(FamiliesDbContext db, Family family, UserId owner)
    {
        Elderly elderly = Elderly.Create(owner, UserId.New(), family.Id, FamilyRelationshipType.Mother,
            FullName.Create("Ù…ØµØ±ÙŠ"), FullName.Create("Elderly"), Gender.Female,
            new DateOnly(1950, 1, 1), ReferenceDate);
        db.Elderlies.Add(elderly);
        return elderly;
    }

    private static ElderlyIntakeRequest Request(UserId userId, FamilyId familyId, ElderlyId elderlyId,
        string? name = null, string? phone = null, string? relationship = null) =>
        new(userId, familyId, elderlyId, ReferenceDate, ResponsibleContactName: name,
            ResponsibleContactPhone: phone, ResponsibleContactRelationship: relationship);

    private sealed class IntakeSender(
        MyProfileResponse profile,
        ElderlyMedicalProfileResponse medical,
        bool failMedical = false) : ISender
    {
        public int MedicalRequests { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetMyProfileQuery)
                return Task.FromResult((TResponse)(object)Result<MyProfileResponse>.Success(profile));
            if (request is GetElderlyMedicalProfileQuery)
            {
                MedicalRequests++;
                if (failMedical)
                {
                    return Task.FromResult((TResponse)(object)Result<ElderlyMedicalProfileResponse>.Failure(
                        new Error("Families.Medical.NotFound", "Medical profile was not found.")));
                }
                return Task.FromResult((TResponse)(object)Result<ElderlyMedicalProfileResponse>.Success(medical));
            }
            throw new NotSupportedException(request.GetType().FullName);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
