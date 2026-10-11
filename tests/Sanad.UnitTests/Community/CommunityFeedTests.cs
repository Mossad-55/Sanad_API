using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Posts;
using Sanad.Modules.Community.Domain.Categories;
using Sanad.Modules.Community.Domain.Interactions;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Domain.Ratings;
using Sanad.Modules.Community.Infrastructure.Persistence;

namespace Sanad.UnitTests.Community;

public sealed class CommunityFeedTests
{
    [Fact]
    public async Task GetPosts_SearchMatchesTitlesAndContentsInBothLanguages()
    {
        await using var db = CreateDb();
        var t0 = DateTime.UtcNow.AddMinutes(-5);
        db.Posts.AddRange(
            CreatePublishedPost("نصيحة صحية", "Health tip", "اشرب الماء", "Drink water", null, t0),
            CreatePublishedPost("عنوان آخر", "Other", "محتوى", "Content", null, t0.AddMinutes(1)));
        await db.SaveChangesAsync();

        var byEnglish = await new GetPostsQueryHandler(db).Handle(
            new GetPostsQuery(PostStatus.Published, Search: "HEALTH"), default);
        var byArabic = await new GetPostsQueryHandler(db).Handle(
            new GetPostsQuery(PostStatus.Published, Search: "الماء"), default);

        Assert.True(byEnglish.IsSuccess);
        Assert.Single(byEnglish.Value);
        Assert.Equal("Health tip", byEnglish.Value[0].Post.TitleEnglish);
        Assert.True(byArabic.IsSuccess);
        Assert.Single(byArabic.Value);
    }

    [Fact]
    public async Task GetPosts_FiltersByCategoryAndDateRange()
    {
        await using var db = CreateDb();
        var category = CommunityCategory.Create("رعاية", "Care", true, DateTime.UtcNow);
        var other = CommunityCategory.Create("نصائح", "Tips", true, DateTime.UtcNow);
        db.Categories.AddRange(category, other);
        var day = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        db.Posts.AddRange(
            CreatePublishedPost("أ", "A", "a", "a", category.Id, day),
            CreatePublishedPost("ب", "B", "b", "b", other.Id, day.AddDays(10)),
            CreatePublishedPost("ج", "C", "c", "c", null, day.AddDays(-10)));
        await db.SaveChangesAsync();

        var byCategory = await new GetPostsQueryHandler(db).Handle(
            new GetPostsQuery(PostStatus.Published, CategoryId: category.Id.Value), default);
        var byRange = await new GetPostsQueryHandler(db).Handle(
            new GetPostsQuery(PostStatus.Published, FromDateUtc: day.AddDays(-1), ToDateUtc: day.AddDays(1)), default);

        Assert.True(byCategory.IsSuccess);
        Assert.Single(byCategory.Value);
        Assert.Equal("A", byCategory.Value[0].Post.TitleEnglish);
        Assert.NotNull(byCategory.Value[0].Post.Category);
        Assert.Equal("Care", byCategory.Value[0].Post.Category!.EnglishName);
        Assert.True(byRange.IsSuccess);
        Assert.Single(byRange.Value);
    }

    [Fact]
    public async Task GetPosts_SortsNewestOldestAndMostLiked()
    {
        await using var db = CreateDb();
        var t0 = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        var older = CreatePublishedPost("أ", "Older", "a", "a", null, t0);
        var newer = CreatePublishedPost("ب", "Newer", "b", "b", null, t0.AddDays(1));
        var popular = CreatePublishedPost("ج", "Popular", "c", "c", null, t0.AddDays(2));
        for (int i = 0; i < 3; i++) popular.IncrementLikes();
        newer.IncrementLikes();
        db.Posts.AddRange(older, newer, popular);
        await db.SaveChangesAsync();

        var newest = await new GetPostsQueryHandler(db).Handle(
            new GetPostsQuery(PostStatus.Published, Sort: CommunityPostsSort.Newest), default);
        var oldest = await new GetPostsQueryHandler(db).Handle(
            new GetPostsQuery(PostStatus.Published, Sort: CommunityPostsSort.Oldest), default);
        var mostLiked = await new GetPostsQueryHandler(db).Handle(
            new GetPostsQuery(PostStatus.Published, Sort: CommunityPostsSort.MostLiked), default);

        Assert.Equal(new[] { "Popular", "Newer", "Older" }, newest.Value.Select(p => p.Post.TitleEnglish).ToArray());
        Assert.Equal(new[] { "Older", "Newer", "Popular" }, oldest.Value.Select(p => p.Post.TitleEnglish).ToArray());
        Assert.Equal(new[] { "Popular", "Newer", "Older" }, mostLiked.Value.Select(p => p.Post.TitleEnglish).ToArray());
    }

    [Fact]
    public async Task GetPosts_AttachesViewerLikeFavoriteAndRating()
    {
        await using var db = CreateDb();
        var viewer = UserId.New();
        var post = CreatePublishedPost("أ", "Post", "a", "a", null, DateTime.UtcNow);
        db.Posts.Add(post);
        db.Interactions.Add(CommunityInteraction.Create(
            CommunityInteractionTarget.Post, post.Id.Value, viewer, CommunityInteractionKind.Like, DateTime.UtcNow));
        db.Interactions.Add(CommunityInteraction.Create(
            CommunityInteractionTarget.Post, post.Id.Value, viewer, CommunityInteractionKind.Favorite, DateTime.UtcNow));
        db.Ratings.Add(Rating.Create(post.Id, viewer, 4, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var withViewer = await new GetPostsQueryHandler(db).Handle(
            new GetPostsQuery(PostStatus.Published, ViewerUserId: viewer.Value), default);
        var withoutViewer = await new GetPostsQueryHandler(db).Handle(
            new GetPostsQuery(PostStatus.Published), default);

        var item = Assert.Single(withViewer.Value);
        Assert.True(item.IsLiked);
        Assert.True(item.IsFavorited);
        Assert.Equal(4, item.CurrentUserRating);
        var anonymous = Assert.Single(withoutViewer.Value);
        Assert.False(anonymous.IsLiked);
        Assert.False(anonymous.IsFavorited);
        Assert.Null(anonymous.CurrentUserRating);
    }

    [Fact]
    public async Task GetFavorites_ReturnsFavoritedPublishedNewestFirst()
    {
        await using var db = CreateDb();
        var viewer = UserId.New();
        var stranger = UserId.New();
        var t0 = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        var first = CreatePublishedPost("أ", "First", "a", "a", null, t0);
        var second = CreatePublishedPost("ب", "Second", "b", "b", null, t0.AddDays(1));
        var pending = Post.Create("ج", "Pending", "c", "c", null, false, UserId.New(), t0);
        db.Posts.AddRange(first, second, pending);
        db.Interactions.Add(CommunityInteraction.Create(
            CommunityInteractionTarget.Post, first.Id.Value, viewer, CommunityInteractionKind.Favorite, t0));
        db.Interactions.Add(CommunityInteraction.Create(
            CommunityInteractionTarget.Post, second.Id.Value, viewer, CommunityInteractionKind.Favorite, t0.AddDays(1)));
        db.Interactions.Add(CommunityInteraction.Create(
            CommunityInteractionTarget.Post, pending.Id.Value, viewer, CommunityInteractionKind.Favorite, t0.AddDays(2)));
        db.Interactions.Add(CommunityInteraction.Create(
            CommunityInteractionTarget.Post, first.Id.Value, stranger, CommunityInteractionKind.Favorite, t0.AddDays(3)));
        await db.SaveChangesAsync();

        var result = await new GetFavoritePostsQueryHandler(db).Handle(
            new GetFavoritePostsQuery(viewer.Value), default);

        Assert.True(result.IsSuccess);
        var items = result.Value;
        Assert.Equal(2, items.Count);
        Assert.Equal("Second", items[0].Post.TitleEnglish);
        Assert.Equal("First", items[1].Post.TitleEnglish);
        Assert.All(items, i => Assert.True(i.IsFavorited));
    }

    [Fact]
    public async Task CreatePost_UnknownCategory_ReturnsInvalidCategory()
    {
        await using var db = CreateDb();

        var result = await new CreatePostCommandHandler(db).Handle(new CreatePostCommand
        {
            TitleArabic = "أ", TitleEnglish = "Post", ContentArabic = "a", ContentEnglish = "a",
            AuthorId = UserId.New().Value,
            CategoryId = Guid.NewGuid()
        }, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Community.InvalidCategory", result.Error.Code);
        Assert.Empty(db.Posts);
    }

    [Fact]
    public async Task CreatePost_InactiveCategory_ReturnsCategoryInactive()
    {
        await using var db = CreateDb();
        var inactive = CommunityCategory.Create("رعاية", "Care", false, DateTime.UtcNow);
        db.Categories.Add(inactive);
        await db.SaveChangesAsync();

        var result = await new CreatePostCommandHandler(db).Handle(new CreatePostCommand
        {
            TitleArabic = "أ", TitleEnglish = "Post", ContentArabic = "a", ContentEnglish = "a",
            AuthorId = UserId.New().Value,
            CategoryId = inactive.Id.Value
        }, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Community.CategoryInactive", result.Error.Code);
        Assert.Empty(db.Posts);
    }

    [Fact]
    public async Task CreatePost_ActiveCategory_IsPersistedOnPost()
    {
        await using var db = CreateDb();
        var active = CommunityCategory.Create("رعاية", "Care", true, DateTime.UtcNow);
        db.Categories.Add(active);
        await db.SaveChangesAsync();

        var result = await new CreatePostCommandHandler(db).Handle(new CreatePostCommand
        {
            TitleArabic = "أ", TitleEnglish = "Post", ContentArabic = "a", ContentEnglish = "a",
            AuthorId = UserId.New().Value,
            CategoryId = active.Id.Value
        }, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(active.Id, result.Value.CategoryId);
        var stored = await db.Posts.SingleAsync();
        Assert.Equal(active.Id, stored.CategoryId);
    }

    [Fact]
    public async Task GetPost_ReturnsNullForUnknownAndAttachesStateForPublished()
    {
        await using var db = CreateDb();
        var viewer = UserId.New();
        var post = CreatePublishedPost("أ", "Post", "a", "a", null, DateTime.UtcNow);
        db.Posts.Add(post);
        db.Interactions.Add(CommunityInteraction.Create(
            CommunityInteractionTarget.Post, post.Id.Value, viewer, CommunityInteractionKind.Like, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var missing = await new GetPostQueryHandler(db).Handle(
            new GetPostQuery(Guid.NewGuid(), viewer.Value), default);
        var found = await new GetPostQueryHandler(db).Handle(
            new GetPostQuery(post.Id.Value, viewer.Value), default);

        Assert.True(missing.IsSuccess);
        Assert.Null(missing.Value);
        Assert.True(found.IsSuccess);
        Assert.NotNull(found.Value);
        Assert.True(found.Value!.IsLiked);
        Assert.False(found.Value.IsFavorited);
        Assert.Null(found.Value.CurrentUserRating);
    }

    private static Post CreatePublishedPost(
        string titleArabic, string titleEnglish,
        string contentArabic, string contentEnglish,
        CommunityCategoryId? categoryId, DateTime createdOnUtc)
    {
        var post = Post.Create(
            titleArabic, titleEnglish, contentArabic, contentEnglish, null, false,
            UserId.New(), createdOnUtc, categoryId);
        post.Publish(UserId.New(), createdOnUtc);
        return post;
    }

    private static CommunityDbContext CreateDb() => new(
        new DbContextOptionsBuilder<CommunityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
