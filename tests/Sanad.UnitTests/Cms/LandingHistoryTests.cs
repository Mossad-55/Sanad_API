using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Cms.Application.Landing;
using Sanad.Modules.Cms.Domain.Landing;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Cms;

public sealed class LandingHistoryTests
{
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public void Model_ShouldMapLandingHistoryTable()
    {
        using CmsDbContext db = CreateCmsContext();

        Assert.Contains(
            db.Model.GetEntityTypes(),
            entityType => entityType.GetTableName() == "landing_section_history");
    }

    [Fact]
    public async Task Publish_WritesHistoryRow_WithActorSectionActionUtc()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext familiesDb = CreateFamiliesContext();
        await using CaregiversDbContext caregiversDb = CreateCaregiversContext();

        LandingSection section = LandingSection.Create(
            LandingSectionType.Footer, "تذييل", "Footer");
        db.LandingSections.Add(section);
        await db.SaveChangesAsync();

        DateTime before = DateTime.UtcNow;
        var published = await new PublishLandingSectionCommandHandler(db, familiesDb, caregiversDb).Handle(
            new PublishLandingSectionCommand(section.Id.Value, ActorId), CancellationToken.None);
        DateTime after = DateTime.UtcNow;

        Assert.True(published.IsSuccess);

        LandingSectionHistory row = Assert.Single(db.LandingSectionHistory);
        Assert.Equal(section.Id, row.SectionId);
        Assert.Equal(LandingHistoryAction.Published, row.Action);
        Assert.Equal(new UserId(ActorId), row.ActorUserId);
        Assert.Null(row.Details);
        Assert.InRange(row.CreatedOnUtc, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public async Task Visibility_WritesHistoryRow_WithValue()
    {
        await using CmsDbContext db = CreateCmsContext();

        LandingSection section = LandingSection.Create(
            LandingSectionType.Steps, "خطوات", "Steps");
        db.LandingSections.Add(section);
        await db.SaveChangesAsync();

        var result = await new SetLandingSectionVisibilityCommandHandler(db).Handle(
            new SetLandingSectionVisibilityCommand(section.Id.Value, false, ActorId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        LandingSectionHistory row = Assert.Single(db.LandingSectionHistory);
        Assert.Equal(LandingHistoryAction.VisibilityChanged, row.Action);
        Assert.Equal("IsVisible=False", row.Details);
    }

    [Fact]
    public async Task Delete_WritesHistoryRow_AndHistorySurvivesSectionDelete()
    {
        await using CmsDbContext db = CreateCmsContext();

        LandingSection section = LandingSection.Create(
            LandingSectionType.About, "من نحن", "About");
        db.LandingSections.Add(section);
        await db.SaveChangesAsync();

        var deleted = await new DeleteLandingSectionCommandHandler(db).Handle(
            new DeleteLandingSectionCommand(section.Id.Value, ActorId), CancellationToken.None);

        Assert.True(deleted.IsSuccess);
        Assert.Empty(db.LandingSections);

        LandingSectionHistory row = Assert.Single(db.LandingSectionHistory);
        Assert.Equal(LandingHistoryAction.Deleted, row.Action);
        Assert.Equal(section.Id, row.SectionId);
        Assert.Contains("about", row.Details);
    }

    [Fact]
    public async Task FailedOperations_WriteNoHistory()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext familiesDb = CreateFamiliesContext();
        await using CaregiversDbContext caregiversDb = CreateCaregiversContext();

        var missing = await new PublishLandingSectionCommandHandler(db, familiesDb, caregiversDb).Handle(
            new PublishLandingSectionCommand(Guid.NewGuid(), ActorId), CancellationToken.None);
        Assert.True(missing.IsFailure);

        LandingSection partial = LandingSection.Create(LandingSectionType.Footer);
        db.LandingSections.Add(partial);
        await db.SaveChangesAsync();

        var incomplete = await new PublishLandingSectionCommandHandler(db, familiesDb, caregiversDb).Handle(
            new PublishLandingSectionCommand(partial.Id.Value, ActorId), CancellationToken.None);
        Assert.True(incomplete.IsFailure);

        var noActor = await new DeleteLandingSectionCommandHandler(db).Handle(
            new DeleteLandingSectionCommand(partial.Id.Value, Guid.Empty), CancellationToken.None);
        Assert.True(noActor.IsFailure);

        Assert.Empty(db.LandingSectionHistory);
    }

    [Fact]
    public async Task History_TranslatesOnRelationalStore()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new CmsDbContext(
            new DbContextOptionsBuilder<CmsDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

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

        LandingSection section = LandingSection.Create(
            LandingSectionType.Footer, "تذييل", "Footer");
        db.LandingSections.Add(section);
        await db.SaveChangesAsync();

        var published = await new PublishLandingSectionCommandHandler(db, familiesDb, caregiversDb).Handle(
            new PublishLandingSectionCommand(section.Id.Value, ActorId), CancellationToken.None);
        Assert.True(published.IsSuccess);

        var hidden = await new SetLandingSectionVisibilityCommandHandler(db).Handle(
            new SetLandingSectionVisibilityCommand(section.Id.Value, false, ActorId),
            CancellationToken.None);
        Assert.True(hidden.IsSuccess);

        List<LandingSectionHistory> rows = await db.LandingSectionHistory
            .AsNoTracking()
            .OrderBy(row => row.CreatedOnUtc)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(LandingHistoryAction.Published, rows[0].Action);
        Assert.Equal(LandingHistoryAction.VisibilityChanged, rows[1].Action);
    }

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
}
