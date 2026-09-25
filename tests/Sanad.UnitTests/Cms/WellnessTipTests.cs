using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Cms.Application.Wellness;
using Sanad.Modules.Cms.Domain.Wellness;
using Sanad.Modules.Cms.Infrastructure.Persistence;

namespace Sanad.UnitTests.Cms;

public sealed class WellnessTipTests
{
    [Fact]
    public async Task PublicFeed_ExcludesDraftAndArchivedTips_AndPreservesOrderedLocalizedSections()
    {
        await using var db = CreateDbContext();
        var draft = await Create(db, "مسودة", "Draft", "sleep");
        var published = await Create(db, "نصيحة منشورة", "Published", "sleep");
        var archived = await Create(db, "نصيحة مؤرشفة", "Archived", "sleep");

        await new PublishWellnessTipCommandHandler(db)
            .Handle(new PublishWellnessTipCommand(published.Id), CancellationToken.None);
        await new PublishWellnessTipCommandHandler(db)
            .Handle(new PublishWellnessTipCommand(archived.Id), CancellationToken.None);
        await new ArchiveWellnessTipCommandHandler(db)
            .Handle(new ArchiveWellnessTipCommand(archived.Id), CancellationToken.None);

        var result = await new ListWellnessTipsQueryHandler(db)
            .Handle(new ListWellnessTipsQuery(null, null, null, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(published.Id, item.Id);
        Assert.DoesNotContain(result.Value.Items, x => x.Id == draft.Id);
        Assert.DoesNotContain(result.Value.Items, x => x.Id == archived.Id);
        Assert.Equal([1, 2], item.Sections.Select(x => x.DisplayOrder));
        Assert.Equal("first ar", item.Sections[0].ArabicText);
        Assert.Equal("second en", item.Sections[1].EnglishText);
    }

    [Fact]
    public async Task PublicDetail_ReturnsNotPublishedForDraftAndArchivedTips()
    {
        await using var db = CreateDbContext();
        var draft = await Create(db, "Draft", "Draft", "sleep");

        var draftResult = await new GetWellnessTipQueryHandler(db)
            .Handle(new GetWellnessTipQuery(draft.Id, true), CancellationToken.None);
        Assert.True(draftResult.IsFailure);
        Assert.Equal(WellnessTipErrors.NotPublished.Code, draftResult.Error.Code);

        await new PublishWellnessTipCommandHandler(db)
            .Handle(new PublishWellnessTipCommand(draft.Id), CancellationToken.None);
        await new ArchiveWellnessTipCommandHandler(db)
            .Handle(new ArchiveWellnessTipCommand(draft.Id), CancellationToken.None);

        var archivedResult = await new GetWellnessTipQueryHandler(db)
            .Handle(new GetWellnessTipQuery(draft.Id, true), CancellationToken.None);
        Assert.True(archivedResult.IsFailure);
        Assert.Equal(WellnessTipErrors.NotPublished.Code, archivedResult.Error.Code);
    }

    [Fact]
    public async Task DraftLifecycle_PersistsAndPublishedContentCannotBeUpdatedOrRepublishedAfterArchive()
    {
        await using var db = CreateDbContext();
        var created = await new CreateWellnessTipCommandHandler(db)
            .Handle(CreateCommand("Draft", "Draft"), CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal(WellnessTipPublicationStatus.Draft, created.Value.Status);
        Assert.Single(db.WellnessTips);
        Assert.Equal(2, db.WellnessTips.Include(x => x.Sections).Single().Sections.Count);

        var updated = await new UpdateWellnessTipCommandHandler(db)
            .Handle(new UpdateWellnessTipCommand(
                created.Value.Id, "Updated", "Updated", "general", "wellness/updated.png",
                [new(1, "updated ar", "updated en")]), CancellationToken.None);
        Assert.True(updated.IsSuccess);
        Assert.Equal("Updated", updated.Value.EnglishTitle);

        var published = await new PublishWellnessTipCommandHandler(db)
            .Handle(new PublishWellnessTipCommand(created.Value.Id), CancellationToken.None);
        Assert.True(published.IsSuccess);

        var updatePublished = await new UpdateWellnessTipCommandHandler(db)
            .Handle(new UpdateWellnessTipCommand(
                created.Value.Id, "Illegal", "Illegal", "general", "wellness/illegal.png",
                [new(1, "illegal ar", "illegal en")]), CancellationToken.None);
        Assert.True(updatePublished.IsFailure);
        Assert.Equal(WellnessTipErrors.InvalidOperation.Code, updatePublished.Error.Code);

        await new ArchiveWellnessTipCommandHandler(db)
            .Handle(new ArchiveWellnessTipCommand(created.Value.Id), CancellationToken.None);
        var republish = await new PublishWellnessTipCommandHandler(db)
            .Handle(new PublishWellnessTipCommand(created.Value.Id), CancellationToken.None);
        Assert.True(republish.IsFailure);
        Assert.Equal(WellnessTipErrors.InvalidOperation.Code, republish.Error.Code);
    }

    [Fact]
    public async Task Create_RejectsEmptyDuplicateOrOutOfRangeSectionsWithoutPersistence()
    {
        await using var db = CreateDbContext();
        var handler = new CreateWellnessTipCommandHandler(db);

        var empty = await handler.Handle(CreateCommand("a", "a") with { Sections = [] }, CancellationToken.None);
        Assert.True(empty.IsFailure);

        var duplicate = await handler.Handle(CreateCommand("b", "b") with
        {
            Sections = [new(1, "one", "one"), new(1, "duplicate", "duplicate")]
        }, CancellationToken.None);
        Assert.True(duplicate.IsFailure);

        var invalidOrder = await handler.Handle(CreateCommand("c", "c") with
        {
            Sections = [new(0, "zero", "zero")]
        }, CancellationToken.None);
        Assert.True(invalidOrder.IsFailure);
        Assert.Empty(db.WellnessTips);
    }

    private static async Task<WellnessTip> Create(CmsDbContext db, string ar, string en, string category)
    {
        var result = await new CreateWellnessTipCommandHandler(db)
            .Handle(CreateCommand(ar, en) with { Category = category }, CancellationToken.None);
        Assert.True(result.IsSuccess);
        return db.WellnessTips.Single(x => x.Id == result.Value.Id);
    }

    private static CreateWellnessTipCommand CreateCommand(string ar, string en) =>
        new(ar, en, "general", "wellness/tip.png",
        [new(2, " second ar ", " second en "), new(1, " first ar ", " first en ")]);

    private static CmsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CmsDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
