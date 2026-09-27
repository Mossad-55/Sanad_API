using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Cms.Application.SentenceBuilder;
using Sanad.Modules.Cms.Domain.SentenceBuilder;
using Sanad.Modules.Cms.Infrastructure.Persistence;

namespace Sanad.UnitTests.Cms;

public sealed class SentenceBuilderCatalogTests
{
    [Fact]
    public async Task Upsert_CreatesImmutableRevisions_AndLeavesExactlyOneActiveRevision()
    {
        await using var db = CreateDb();
        var handler = new UpsertSentenceBuilderCatalogCommandHandler(db);

        var first = await handler.Handle(new("elderly", SentenceBuilderCategory.Actor, "مسن", "Elderly", 0), default);
        var second = await handler.Handle(new(" elderly ", SentenceBuilderCategory.Actor, "أنا", "I", 1), default);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        var entry = await db.SentenceBuilderCatalogEntries.Include(x => x.Revisions).SingleAsync();
        Assert.Equal("elderly", entry.StableKey);
        Assert.Equal(2, entry.Revisions.Count);
        Assert.Equal(2, entry.Revisions.Max(x => x.Version));
        Assert.Single(entry.Revisions, x => x.IsActive);
        Assert.Equal(second.Value.RevisionId, entry.Revisions.Single(x => x.IsActive).Id);
        Assert.Equal(first.Value.ArabicLabel, entry.Revisions.Single(x => x.Version == 1).ArabicLabel);
    }

    [Fact]
    public async Task SetActive_SwitchesRevisionWithoutMutatingHistoricalLabels()
    {
        await using var db = CreateDb();
        var first = await new UpsertSentenceBuilderCatalogCommandHandler(db).Handle(
            new("need", SentenceBuilderCategory.Need, "ماء", "Water", 0), default);
        var second = await new UpsertSentenceBuilderCatalogCommandHandler(db).Handle(
            new("need", SentenceBuilderCategory.Need, "دواء", "Medicine", 1), default);

        var result = await new SetSentenceBuilderCatalogActiveCommandHandler(db).Handle(
            new(first.Value.RevisionId, true), default);

        Assert.True(result.IsSuccess);
        var revisions = await db.SentenceBuilderCatalogRevisions.OrderBy(x => x.Version).ToListAsync();
        Assert.True(revisions[0].IsActive);
        Assert.False(revisions[1].IsActive);
        Assert.Equal("Water", revisions[0].EnglishLabel);
        Assert.Equal("Medicine", revisions[1].EnglishLabel);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Upsert_RejectsBlankStableKeys(string key)
    {
        await using var db = CreateDb();
        var result = await new UpsertSentenceBuilderCatalogCommandHandler(db).Handle(
            new(key, SentenceBuilderCategory.Actor, "مسن", "Elderly", 0), default);

        Assert.True(result.IsFailure);
        Assert.Equal(SentenceBuilderCatalogErrors.Invalid.Code, result.Error.Code);
        Assert.Empty(db.SentenceBuilderCatalogEntries);
    }

    [Fact]
    public async Task Upsert_RejectsInvalidCategoryAndNegativeOrder()
    {
        await using var db = CreateDb();
        var invalidCategory = await new UpsertSentenceBuilderCatalogCommandHandler(db).Handle(
            new("bad-category", (SentenceBuilderCategory)99, "نص", "Text", 0), default);
        var negativeOrder = await new UpsertSentenceBuilderCatalogCommandHandler(db).Handle(
            new("bad-order", SentenceBuilderCategory.Need, "نص", "Text", -1), default);

        Assert.True(invalidCategory.IsFailure);
        Assert.True(negativeOrder.IsFailure);
    }

    private static CmsDbContext CreateDb() => new(new DbContextOptionsBuilder<CmsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
