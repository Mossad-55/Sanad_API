using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Categories;
using Sanad.Modules.Community.Domain.Categories;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Infrastructure.Persistence;

namespace Sanad.UnitTests.Community;

public sealed class CommunityCategoryTests
{
    [Fact]
    public void Create_TrimsNamesAndDefaultsActive()
    {
        var category = CommunityCategory.Create(
            "  رعاية  ", "  Care  ", true, DateTime.UtcNow);

        Assert.Equal("رعاية", category.ArabicName);
        Assert.Equal("Care", category.EnglishName);
        Assert.True(category.IsActive);
        Assert.Null(category.UpdatedOnUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsEmptyNames(string name)
    {
        Assert.Throws<DomainException>(() => CommunityCategory.Create(
            name, "Care", true, DateTime.UtcNow));
        Assert.Throws<DomainException>(() => CommunityCategory.Create(
            "رعاية", name, true, DateTime.UtcNow));
    }

    [Fact]
    public void Update_RenamesAndTogglesActive()
    {
        var category = CommunityCategory.Create("رعاية", "Care", true, DateTime.UtcNow);
        var now = DateTime.UtcNow;

        category.Update("نصيحة", "Tips", false, now);

        Assert.Equal("نصيحة", category.ArabicName);
        Assert.Equal("Tips", category.EnglishName);
        Assert.False(category.IsActive);
        Assert.Equal(now, category.UpdatedOnUtc);
    }

    [Fact]
    public async Task CreateCategory_PersistsAndReturnsResponse()
    {
        await using var db = CreateDb();

        var result = await new CreateCategoryCommandHandler(db).Handle(
            new CreateCategoryCommand("رعاية", "Care", true), default);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
        var stored = await db.Categories.SingleAsync();
        Assert.Equal(result.Value.Id, stored.Id.Value);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task CreateCategory_InvalidNames_ReturnsInvalidError()
    {
        await using var db = CreateDb();

        var result = await new CreateCategoryCommandHandler(db).Handle(
            new CreateCategoryCommand("  ", "Care"), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Community.Category.Invalid", result.Error.Code);
        Assert.Empty(db.Categories);
    }

    [Fact]
    public async Task UpdateCategory_Unknown_ReturnsNotFound()
    {
        await using var db = CreateDb();

        var result = await new UpdateCategoryCommandHandler(db).Handle(
            new UpdateCategoryCommand(Guid.NewGuid(), "رعاية", "Care", true), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Community.CategoryNotFound", result.Error.Code);
    }

    [Fact]
    public async Task DeleteCategory_Unknown_ReturnsNotFound()
    {
        await using var db = CreateDb();

        var result = await new DeleteCategoryCommandHandler(db).Handle(
            new DeleteCategoryCommand(Guid.NewGuid()), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Community.CategoryNotFound", result.Error.Code);
    }

    [Fact]
    public async Task DeleteCategory_ReferencedByPost_ReturnsInUse()
    {
        await using var db = CreateDb();
        var category = CommunityCategory.Create("رعاية", "Care", true, DateTime.UtcNow);
        db.Categories.Add(category);
        db.Posts.Add(Post.Create(
            "عنوان", "Title", "محتوى", "Content", null, false, UserId.New(),
            DateTime.UtcNow, category.Id));
        await db.SaveChangesAsync();

        var result = await new DeleteCategoryCommandHandler(db).Handle(
            new DeleteCategoryCommand(category.Id.Value), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Community.CategoryInUse", result.Error.Code);
        Assert.Single(db.Categories);
    }

    [Fact]
    public async Task DeleteCategory_Unreferenced_RemovesRow()
    {
        await using var db = CreateDb();
        var category = CommunityCategory.Create("رعاية", "Care", true, DateTime.UtcNow);
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var result = await new DeleteCategoryCommandHandler(db).Handle(
            new DeleteCategoryCommand(category.Id.Value), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(db.Categories);
    }

    [Fact]
    public async Task GetActiveCategories_ReturnsOnlyActiveOrdered()
    {
        await using var db = CreateDb();
        var older = CommunityCategory.Create("أ", "A", true, DateTime.UtcNow.AddMinutes(-2));
        var newer = CommunityCategory.Create("ب", "B", true, DateTime.UtcNow.AddMinutes(-1));
        var inactive = CommunityCategory.Create("ج", "C", false, DateTime.UtcNow);
        db.Categories.AddRange(older, newer, inactive);
        await db.SaveChangesAsync();

        var result = await new GetActiveCategoriesQueryHandler(db).Handle(
            new GetActiveCategoriesQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("A", result.Value[0].EnglishName);
        Assert.Equal("B", result.Value[1].EnglishName);
    }

    [Fact]
    public async Task GetCategoriesAdmin_ReturnsAllIncludingInactive()
    {
        await using var db = CreateDb();
        db.Categories.AddRange(
            CommunityCategory.Create("أ", "A", true, DateTime.UtcNow),
            CommunityCategory.Create("ج", "C", false, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var result = await new GetCommunityCategoriesAdminQueryHandler(db).Handle(
            new GetCommunityCategoriesAdminQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(result.Value, c => !c.IsActive);
    }

    private static CommunityDbContext CreateDb() => new(
        new DbContextOptionsBuilder<CommunityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
