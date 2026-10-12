using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Cms.Application.Landing;
using Sanad.Modules.Cms.Domain.Landing;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.UnitTests.Caregivers;
using Sanad.UnitTests.CareHomes;

namespace Sanad.UnitTests.Cms;

public sealed class LandingReadsTests
{
    [Fact]
    public void Model_ShouldMapLandingSectionsTable()
    {
        using CmsDbContext db = CreateCmsContext();

        Assert.Contains(
            db.Model.GetEntityTypes(),
            entityType => entityType.GetTableName() == "landing_sections");
    }

    [Fact]
    public async Task Sections_EmptyList_WhenNothingPublished()
    {
        await using CmsDbContext db = CreateCmsContext();

        var result = await new GetPublishedLandingSectionsQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetPublishedLandingSectionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Sections_ReturnsVisiblePublishedOrdered_ExcludesDraftHiddenVirtual()
    {
        await using CmsDbContext db = CreateCmsContext();

        LandingSection hero = LandingSection.Create(
            LandingSectionType.Hero,
            arabicTitle: "بطل",
            englishTitle: "Hero",
            displayOrder: 1);
        hero.Publish();

        LandingSection about = LandingSection.Create(
            LandingSectionType.About,
            arabicTitle: "من نحن",
            englishTitle: "About",
            arabicBody: "نص",
            englishBody: "Body",
            displayOrder: 0);
        about.Publish();

        LandingSection draft = LandingSection.Create(
            LandingSectionType.Features,
            arabicTitle: "مسودة",
            englishTitle: "Draft");

        LandingSection hidden = LandingSection.Create(
            LandingSectionType.Steps,
            arabicTitle: "مخفي",
            englishTitle: "Hidden");
        hidden.Publish();
        hidden.SetVisibility(false);

        db.LandingSections.AddRange(hero, about, draft, hidden);
        await db.SaveChangesAsync();

        var result = await new GetPublishedLandingSectionsQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetPublishedLandingSectionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("about", result.Value[0].Type);
        Assert.Equal("hero", result.Value[1].Type);
        Assert.Equal("Body", result.Value[0].EnglishBody);
    }

    [Fact]
    public void Sections_Publish_RequiresBilingualTitles_AndRejectsVirtualStats()
    {
        LandingSection partial = LandingSection.Create(LandingSectionType.Footer);

        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(
            () => partial.Publish());

        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(
            () => LandingSection.Create(LandingSectionType.Stats));
    }

    [Fact]
    public async Task Sections_TranslatesOnRelationalStore()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new CmsDbContext(
            new DbContextOptionsBuilder<CmsDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        LandingSection about = LandingSection.Create(
            LandingSectionType.About,
            arabicTitle: "من نحن",
            englishTitle: "About",
            displayOrder: 0);
        about.Publish();
        db.LandingSections.Add(about);
        await db.SaveChangesAsync();

        var result = await new GetPublishedLandingSectionsQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetPublishedLandingSectionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        LandingSectionPublicItem single = Assert.Single(result.Value);
        Assert.Equal("about", single.Type);
    }

    [Fact]
    public async Task Stats_EmptyStores_ReturnZeros()
    {
        await using FamiliesDbContext familiesDb = CreateFamiliesContext();
        await using CaregiversDbContext caregiversDb = CreateCaregiversContext();
        await using CareHomesDbContext careHomesDb = CreateCareHomesContext();

        var result = await new GetLandingStatsQueryHandler(familiesDb, caregiversDb, careHomesDb).Handle(
            new GetLandingStatsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.FamiliesCount);
        Assert.Equal(0, result.Value.MedicalCaregiversCount);
        Assert.Equal(0, result.Value.CompanionCaregiversCount);
        Assert.Equal(0, result.Value.ApprovedFacilitiesCount);
    }

    [Fact]
    public async Task Stats_CountsExcludeDeletedNonActiveAndDraft()
    {
        await using FamiliesDbContext familiesDb = CreateFamiliesContext();
        await using CaregiversDbContext caregiversDb = CreateCaregiversContext();
        await using CareHomesDbContext careHomesDb = CreateCareHomesContext();

        familiesDb.Families.Add(Family.Create(UserId.New(), "Family one"));
        familiesDb.Families.Add(Family.Create(UserId.New(), "Family two"));
        Family deleted = Family.Create(UserId.New(), "Deleted");
        deleted.MarkDeleted("requested", null);
        familiesDb.Families.Add(deleted);
        await familiesDb.SaveChangesAsync();

        Caregiver medical = Caregiver.Create(UserId.New(), CaregiverType.Medical);
        medical.TransitionToActive();
        Caregiver companion = Caregiver.Create(UserId.New(), CaregiverType.Companion);
        companion.TransitionToActive();
        caregiversDb.Caregivers.AddRange(
            medical,
            companion,
            Caregiver.Create(UserId.New(), CaregiverType.Medical));
        await caregiversDb.SaveChangesAsync();

        careHomesDb.Facilities.Add(CreateApprovedFacility());
        careHomesDb.Facilities.Add(CareHomeFacility.CreateDraft(UserId.New(), UtcNow()));
        await careHomesDb.SaveChangesAsync();

        var result = await new GetLandingStatsQueryHandler(familiesDb, caregiversDb, careHomesDb).Handle(
            new GetLandingStatsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.FamiliesCount);
        Assert.Equal(1, result.Value.MedicalCaregiversCount);
        Assert.Equal(1, result.Value.CompanionCaregiversCount);
        Assert.Equal(1, result.Value.ApprovedFacilitiesCount);
    }

    [Fact]
    public async Task Stats_TranslatesOnRelationalStore()
    {
        using var familiesConnection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await familiesConnection.OpenAsync();
        await using var familiesDb = new FamiliesDbContext(
            new DbContextOptionsBuilder<FamiliesDbContext>().UseSqlite(familiesConnection).Options);
        await familiesDb.Database.EnsureCreatedAsync();

        using var caregiversConnection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await caregiversConnection.OpenAsync();
        await using var caregiversDb = new CaregiversDbContext(
            new DbContextOptionsBuilder<CaregiversDbContext>().UseSqlite(caregiversConnection).Options);
        await caregiversDb.Database.EnsureCreatedAsync();

        using var careHomesConnection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await careHomesConnection.OpenAsync();
        await using var careHomesDb = new CareHomesDbContext(
            new DbContextOptionsBuilder<CareHomesDbContext>().UseSqlite(careHomesConnection).Options);
        await careHomesDb.Database.EnsureCreatedAsync();

        familiesDb.Families.Add(Family.Create(UserId.New(), "Relational family"));
        await familiesDb.SaveChangesAsync();

        // Active caregivers carry schedule windows whose ids are database-generated
        // on Npgsql and violate NOT NULL on SQLite (pre-existing Caregivers mapping
        // gap, out of Phase 1 scope). Onboarding rows persist on both providers and
        // still prove the Status/Type predicates translate; Active-row semantics are
        // proven by the InMemory test above.
        caregiversDb.Caregivers.Add(Caregiver.Create(UserId.New(), CaregiverType.Medical));
        caregiversDb.Caregivers.Add(Caregiver.Create(UserId.New(), CaregiverType.Companion));
        await caregiversDb.SaveChangesAsync();

        careHomesDb.Facilities.Add(CreateApprovedFacility());
        await careHomesDb.SaveChangesAsync();

        var result = await new GetLandingStatsQueryHandler(familiesDb, caregiversDb, careHomesDb).Handle(
            new GetLandingStatsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.FamiliesCount);
        Assert.Equal(0, result.Value.MedicalCaregiversCount);
        Assert.Equal(0, result.Value.CompanionCaregiversCount);
        Assert.Equal(1, result.Value.ApprovedFacilitiesCount);
    }

    private static CareHomeFacility CreateApprovedFacility()
    {
        DateTime now = UtcNow();
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, now);
        facility.SaveDraft(owner, facility.Version, CareHomeFacilityTests.Draft(), now);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"private/{type}.pdf", "application/pdf", 100, null, now);
        facility.Submit(owner, facility.Version, now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(admin, facility.Version, document.Id, null, true, now);
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, now, DateOnly.FromDateTime(now));
        return facility;
    }

    private static DateTime UtcNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

    private static CmsDbContext CreateCmsContext()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CmsDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static FamiliesDbContext CreateFamiliesContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new FamiliesDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static CaregiversDbContext CreateCaregiversContext()
    {
        var options = new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CaregiversDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static CareHomesDbContext CreateCareHomesContext()
    {
        var options = new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CareHomesDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
