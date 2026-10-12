using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Cms.Application.Landing;
using Sanad.Modules.Cms.Domain.Landing;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Cms;

public sealed class LandingAdminTests
{
    [Fact]
    public async Task AdminList_IncludesDrafts_Filtered_Paged()
    {
        await using CmsDbContext db = CreateCmsContext();

        LandingSection hero = DraftOf(LandingSectionType.Hero, "بطل", "Hero", displayOrder: 1);
        LandingSection about = DraftOf(LandingSectionType.About, "من نحن", "About", displayOrder: 0);
        about.Publish();
        db.LandingSections.AddRange(hero, about);
        await db.SaveChangesAsync();

        var all = await new GetLandingAdminListQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetLandingAdminListQuery(), CancellationToken.None);
        Assert.True(all.IsSuccess);
        Assert.Equal(2, all.Value.TotalCount);

        var drafts = await new GetLandingAdminListQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetLandingAdminListQuery(Status: LandingSectionStatus.Draft), CancellationToken.None);
        Assert.True(drafts.IsSuccess);
        var draft = Assert.Single(drafts.Value.Items);
        Assert.Equal("hero", draft.Type);

        var page = await new GetLandingAdminListQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetLandingAdminListQuery(Page: 2, PageSize: 1), CancellationToken.None);
        Assert.True(page.IsSuccess);
        Assert.Equal(2, page.Value.TotalCount);
        Assert.Single(page.Value.Items);
    }

    [Fact]
    public async Task Create_PartialDraft_PublishRequiresCompleteness()
    {
        await using CmsDbContext db = CreateCmsContext();

        var created = await new CreateLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new CreateLandingSectionCommand(
                LandingSectionType.Footer, null, null, null, null, null, null, 0, true),
            CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Equal("Draft", created.Value.Status);

        var incomplete = await new PublishLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new PublishLandingSectionCommand(created.Value.Id, ActorId), CancellationToken.None);
        Assert.True(incomplete.IsFailure);
        Assert.Equal("Cms.LandingSection.Incomplete", incomplete.Error.Code);

        var hero = await new CreateLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new CreateLandingSectionCommand(
                LandingSectionType.Hero, "بطل", "Hero", null, null, null, null, 0, true),
            CancellationToken.None);
        Assert.True(hero.IsSuccess);

        var heroIncomplete = await new PublishLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new PublishLandingSectionCommand(hero.Value.Id, ActorId), CancellationToken.None);
        Assert.True(heroIncomplete.IsFailure);
        Assert.Contains("cover", heroIncomplete.Error.Message, StringComparison.OrdinalIgnoreCase);

        var missing = await new PublishLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new PublishLandingSectionCommand(Guid.NewGuid(), ActorId), CancellationToken.None);
        Assert.True(missing.IsFailure);
        Assert.Equal("Cms.LandingSection.NotFound", missing.Error.Code);
    }

    [Fact]
    public async Task Put_OnPublished_CreatesTwin_ThenUpdatesTwin()
    {
        await using CmsDbContext db = CreateCmsContext();

        LandingSection about = DraftOf(LandingSectionType.About, "من نحن", "About");
        about.Publish();
        db.LandingSections.Add(about);
        await db.SaveChangesAsync();

        var first = await new UpdateLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new UpdateLandingSectionCommand(
                about.Id.Value, "من نحن 2", "About 2", null, null, null, null, 0, true),
            CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.NotEqual(about.Id.Value, first.Value.Id);
        Assert.Equal("Draft", first.Value.Status);
        Assert.Equal(about.Id.Value, first.Value.SourceId);

        var second = await new UpdateLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new UpdateLandingSectionCommand(
                about.Id.Value, "من نحن 3", "About 3", null, null, null, null, 0, true),
            CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Id, second.Value.Id);
        Assert.Equal("About 3", second.Value.EnglishTitle);
    }

    [Fact]
    public async Task Publish_Twin_ArchivesPreviousPublished()
    {
        await using CmsDbContext db = CreateCmsContext();

        LandingSection live = DraftOf(LandingSectionType.Footer, "قديم", "Old");
        live.Publish();
        LandingSection twin = LandingSection.Create(
            LandingSectionType.Footer, "جديد", "New", sourceId: live.Id);
        db.LandingSections.AddRange(live, twin);
        await db.SaveChangesAsync();

        var published = await new PublishLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new PublishLandingSectionCommand(twin.Id.Value, ActorId), CancellationToken.None);
        Assert.True(published.IsSuccess);

        var rows = await db.LandingSections.AsNoTracking().ToListAsync();
        Assert.Equal(LandingSectionStatus.Archived, rows.Single(row => row.Id == live.Id).Status);
        Assert.Equal(LandingSectionStatus.Published, rows.Single(row => row.Id == twin.Id).Status);

        var reads = await new GetPublishedLandingSectionsQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetPublishedLandingSectionsQuery(), CancellationToken.None);
        Assert.True(reads.IsSuccess);
        var single = Assert.Single(reads.Value);
        Assert.Equal("New", single.EnglishTitle);
    }

    [Fact]
    public async Task Delete_DraftOnly_PublishedHistoryRetained()
    {
        await using CmsDbContext db = CreateCmsContext();

        LandingSection draft = DraftOf(LandingSectionType.Steps, "مسودة", "Draft");
        LandingSection live = DraftOf(LandingSectionType.Faq, "سؤال", "Faq");
        live.Publish();
        db.LandingSections.AddRange(draft, live);
        await db.SaveChangesAsync();

        var deleted = await new DeleteLandingSectionCommandHandler(db).Handle(
            new DeleteLandingSectionCommand(draft.Id.Value, ActorId), CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        var published = await new DeleteLandingSectionCommandHandler(db).Handle(
            new DeleteLandingSectionCommand(live.Id.Value, ActorId), CancellationToken.None);
        Assert.True(published.IsFailure);
        Assert.Equal("Cms.LandingSection.PublishedHistory", published.Error.Code);

        var missing = await new DeleteLandingSectionCommandHandler(db).Handle(
            new DeleteLandingSectionCommand(Guid.NewGuid(), ActorId), CancellationToken.None);
        Assert.True(missing.IsFailure);
        Assert.Equal("Cms.LandingSection.NotFound", missing.Error.Code);
    }

    [Fact]
    public async Task Visibility_Toggle_Persisted()
    {
        await using CmsDbContext db = CreateCmsContext();

        LandingSection live = DraftOf(LandingSectionType.Steps, "خطوات", "Steps");
        live.Publish();
        db.LandingSections.Add(live);
        await db.SaveChangesAsync();

        var hidden = await new SetLandingSectionVisibilityCommandHandler(db).Handle(
            new SetLandingSectionVisibilityCommand(live.Id.Value, false, ActorId), CancellationToken.None);
        Assert.True(hidden.IsSuccess);

        var reads = await new GetPublishedLandingSectionsQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetPublishedLandingSectionsQuery(), CancellationToken.None);
        Assert.True(reads.IsSuccess);
        Assert.Empty(reads.Value);

        var missing = await new SetLandingSectionVisibilityCommandHandler(db).Handle(
            new SetLandingSectionVisibilityCommand(Guid.NewGuid(), true, ActorId), CancellationToken.None);
        Assert.True(missing.IsFailure);
    }

    [Fact]
    public async Task Reorder_Deterministic_RejectsDuplicatesAndUnknown()
    {
        await using CmsDbContext db = CreateCmsContext();

        LandingSection first = DraftOf(LandingSectionType.Hero, "بطل", "Hero", displayOrder: 0);
        first.Publish();
        LandingSection second = DraftOf(LandingSectionType.About, "من نحن", "About", displayOrder: 1);
        second.Publish();
        db.LandingSections.AddRange(first, second);
        await db.SaveChangesAsync();

        var reordered = await new ReorderLandingSectionsCommandHandler(db).Handle(
            new ReorderLandingSectionsCommand([
                new LandingReorderItem(first.Id.Value, 5),
                new LandingReorderItem(second.Id.Value, 2)
            ]),
            CancellationToken.None);
        Assert.True(reordered.IsSuccess);

        var reads = await new GetPublishedLandingSectionsQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetPublishedLandingSectionsQuery(), CancellationToken.None);
        Assert.True(reads.IsSuccess);
        Assert.Equal("about", reads.Value[0].Type);
        Assert.Equal("hero", reads.Value[1].Type);

        var duplicate = await new ReorderLandingSectionsCommandHandler(db).Handle(
            new ReorderLandingSectionsCommand([
                new LandingReorderItem(first.Id.Value, 0),
                new LandingReorderItem(first.Id.Value, 1)
            ]),
            CancellationToken.None);
        Assert.True(duplicate.IsFailure);
        Assert.Equal("Cms.Landing.ReorderInvalid", duplicate.Error.Code);

        var unknown = await new ReorderLandingSectionsCommandHandler(db).Handle(
            new ReorderLandingSectionsCommand([new LandingReorderItem(Guid.NewGuid(), 0)]),
            CancellationToken.None);
        Assert.True(unknown.IsFailure);
        Assert.Equal("Cms.Landing.ReorderInvalid", unknown.Error.Code);
    }

    [Fact]
    public async Task AdminList_TranslatesOnRelationalStore()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new CmsDbContext(
            new DbContextOptionsBuilder<CmsDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        LandingSection about = DraftOf(LandingSectionType.About, "من نحن", "About");
        about.Publish();
        db.LandingSections.Add(about);
        await db.SaveChangesAsync();

        var result = await new GetLandingAdminListQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetLandingAdminListQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task AdminCommands_TranslateOnRelationalStore()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new CmsDbContext(
            new DbContextOptionsBuilder<CmsDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var created = await new CreateLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new CreateLandingSectionCommand(
                LandingSectionType.Footer, "تذييل", "Footer", null, null, null, null, 0, true),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        var published = await new PublishLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new PublishLandingSectionCommand(created.Value.Id, ActorId), CancellationToken.None);
        Assert.True(published.IsSuccess);

        var twin = await new UpdateLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new UpdateLandingSectionCommand(
                created.Value.Id, "تذييل 2", "Footer 2", null, null, null, null, 0, true),
            CancellationToken.None);
        Assert.True(twin.IsSuccess);
        Assert.Equal("Draft", twin.Value.Status);

        var republished = await new PublishLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new PublishLandingSectionCommand(twin.Value.Id, ActorId), CancellationToken.None);
        Assert.True(republished.IsSuccess);

        var deleted = await new DeleteLandingSectionCommandHandler(db).Handle(
            new DeleteLandingSectionCommand(created.Value.Id, ActorId), CancellationToken.None);
        Assert.True(deleted.IsFailure);

        var reordered = await new ReorderLandingSectionsCommandHandler(db).Handle(
            new ReorderLandingSectionsCommand([new LandingReorderItem(twin.Value.Id, 3)]),
            CancellationToken.None);
        Assert.True(reordered.IsSuccess);
    }

    private static readonly Guid ActorId = Guid.NewGuid();

    private static LandingSection DraftOf(
        LandingSectionType type, string arabicTitle, string englishTitle, int displayOrder = 0) =>
        LandingSection.Create(type, arabicTitle, englishTitle, displayOrder: displayOrder);

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
