using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Application.Blocks;
using Sanad.Modules.Community.Application.Comments;
using Sanad.Modules.Community.Application.Posts;
using Sanad.Modules.Community.Domain.Blocks;
using Sanad.Modules.Community.Domain.Categories;
using Sanad.Modules.Community.Domain.CheckIns;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.Modules.Community.Domain.Interactions;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Domain.Ratings;
using Sanad.Modules.Community.Domain.Uploads;
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

    [Fact]
    public async Task GetPosts_WithViewer_TranslatesOnRelationalStore()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new CommunityDbContext(
            new DbContextOptionsBuilder<CommunityDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var viewer = UserId.New();
        var post = CreatePublishedPost("أ", "Post", "a", "a", null, DateTime.UtcNow);
        db.Posts.Add(post);
        db.Interactions.Add(CommunityInteraction.Create(
            CommunityInteractionTarget.Post, post.Id.Value, viewer, CommunityInteractionKind.Like, DateTime.UtcNow));
        db.Ratings.Add(Rating.Create(post.Id, viewer, 4, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var withViewer = await new GetPostsQueryHandler(db).Handle(
            new GetPostsQuery(PostStatus.Published, ViewerUserId: viewer.Value), default);

        Assert.True(withViewer.IsSuccess);
        var item = Assert.Single(withViewer.Value);
        Assert.True(item.IsLiked);
        Assert.Equal(4, item.CurrentUserRating);
    }

    [Fact]
    public async Task Post_feed_projects_the_author_display_name_and_never_identifies_an_anonymous_author()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var named = UserId.New();
        var hidden = UserId.New();
        var unknown = UserId.New();
        await harness.AddIdentityUserAsync(named.Value, "الكاتب", "Named Author");
        await harness.AddIdentityUserAsync(hidden.Value, "المخفي", "Hidden Author");

        var visible = await harness.AddPublishedPostAsync(named.Value, isAnonymous: false);
        var anonymous = await harness.AddPublishedPostAsync(hidden.Value, isAnonymous: true);
        var withoutIdentity = await harness.AddPublishedPostAsync(unknown.Value, isAnonymous: false);

        CommunityPostsController controller = harness.CreatePostsController(UserId.New());
        IReadOnlyList<CommunityPostResponse> items = harness.ReadItems(
            await controller.GetPosts(cancellationToken: default));

        CommunityPostResponse visibleItem = Assert.Single(items, item => item.Id == visible);
        Assert.Equal("Named Author", visibleItem.DisplayName);
        Assert.Null(visibleItem.AvatarUrl);

        // An anonymous post carries no author identity at all: no display
        // name, no avatar and no author id.
        CommunityPostResponse anonymousItem = Assert.Single(items, item => item.Id == anonymous);
        Assert.True(anonymousItem.IsAnonymous);
        Assert.Null(anonymousItem.DisplayName);
        Assert.Null(anonymousItem.AvatarUrl);
        string anonymousJson = harness.Serialize(anonymousItem);
        Assert.Contains("\"displayName\":null", anonymousJson);
        Assert.Contains("\"avatarUrl\":null", anonymousJson);
        Assert.DoesNotContain("Hidden Author", anonymousJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("المخفي", anonymousJson, StringComparison.Ordinal);
        Assert.DoesNotContain(hidden.Value.ToString(), anonymousJson, StringComparison.OrdinalIgnoreCase);

        // A missing identity row degrades to an honest null, never to a
        // placeholder and never to an exception.
        CommunityPostResponse unknownItem = Assert.Single(items, item => item.Id == withoutIdentity);
        Assert.Null(unknownItem.DisplayName);
        Assert.Null(unknownItem.AvatarUrl);
    }

    [Fact]
    public async Task Post_feed_resolves_the_author_names_with_one_batched_lookup_per_page()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var first = UserId.New();
        var second = UserId.New();
        var hidden = UserId.New();
        await harness.AddIdentityUserAsync(first.Value, "الأول", "First Author");
        await harness.AddIdentityUserAsync(second.Value, "الثاني", "Second Author");
        await harness.AddIdentityUserAsync(hidden.Value, "المخفي", "Hidden Author");

        await harness.AddPublishedPostAsync(first.Value, isAnonymous: false);
        await harness.AddPublishedPostAsync(second.Value, isAnonymous: false);
        await harness.AddPublishedPostAsync(hidden.Value, isAnonymous: true);

        CommunityPostsController controller = harness.CreatePostsController(UserId.New());
        harness.ReadItems(await controller.GetPosts(cancellationToken: default));

        Assert.Equal(1, harness.AuthorNameLookupCount);
        Assert.Equal(2, harness.LastRequestedAuthorIds.Count);
        Assert.Contains(first.Value, harness.LastRequestedAuthorIds);
        Assert.Contains(second.Value, harness.LastRequestedAuthorIds);
        Assert.DoesNotContain(hidden.Value, harness.LastRequestedAuthorIds);
    }

    [Fact]
    public async Task Post_detail_projects_the_author_display_name_and_hides_an_anonymous_author()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var named = UserId.New();
        var hidden = UserId.New();
        await harness.AddIdentityUserAsync(named.Value, "الكاتب", "Named Author");
        await harness.AddIdentityUserAsync(hidden.Value, "المخفي", "Hidden Author");

        Guid visible = await harness.AddPublishedPostAsync(named.Value, isAnonymous: false);
        Guid anonymous = await harness.AddPublishedPostAsync(hidden.Value, isAnonymous: true);

        CommunityPostsController controller = harness.CreatePostsController(UserId.New());

        var visibleResult = await controller.GetPost(visible, default);
        var visibleItem = Assert.IsType<OkObjectResult>(visibleResult).Value;
        var visibleResponse = Assert.IsType<CommunityPostResponse>(visibleItem);
        Assert.Equal("Named Author", visibleResponse.DisplayName);

        var anonymousResult = await controller.GetPost(anonymous, default);
        var anonymousResponse = Assert.IsType<CommunityPostResponse>(
            Assert.IsType<OkObjectResult>(anonymousResult).Value);
        Assert.True(anonymousResponse.IsAnonymous);
        Assert.Null(anonymousResponse.DisplayName);
        Assert.Null(anonymousResponse.AvatarUrl);
        string anonymousJson = harness.Serialize(anonymousResponse);
        Assert.DoesNotContain("Hidden Author", anonymousJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(hidden.Value.ToString(), anonymousJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Blocked_author_posts_are_excluded_from_the_feed()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var viewer = UserId.New();
        var blocked = UserId.New();
        var other = UserId.New();
        await harness.AddIdentityUserAsync(blocked.Value, "محظور", "Blocked Author");
        await harness.AddIdentityUserAsync(other.Value, "آخر", "Other Author");

        Guid blockedPost = await harness.AddPublishedPostAsync(blocked.Value, isAnonymous: false);
        Guid otherPost = await harness.AddPublishedPostAsync(other.Value, isAnonymous: false);
        await harness.BlockAuthorAsync(viewer, blocked);

        CommunityPostsController controller = harness.CreatePostsController(viewer);
        IReadOnlyList<CommunityPostResponse> items = harness.ReadItems(
            await controller.GetPosts(pageSize: 20, cancellationToken: default));

        Assert.DoesNotContain(items, item => item.Id == blockedPost);
        Assert.Contains(items, item => item.Id == otherPost);
        Assert.Single(items);
    }

    [Fact]
    public async Task Blocked_author_posts_are_excluded_from_the_recommendations()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var viewer = UserId.New();
        var blocked = UserId.New();
        await harness.AddIdentityUserAsync(blocked.Value, "محظور", "Blocked Author");

        Guid blockedPost = await harness.AddPublishedPostAsync(blocked.Value, isAnonymous: false);
        await harness.BlockAuthorAsync(viewer, blocked);

        CommunityPostsController controller = harness.CreatePostsController(viewer);
        IReadOnlyList<CommunityPostResponse> items = harness.ReadItems(
            await controller.GetRecommendations(pageSize: 10, cancellationToken: default));

        Assert.DoesNotContain(items, item => item.Id == blockedPost);
        Assert.Empty(items);
    }

    [Fact]
    public async Task Blocked_author_posts_are_excluded_from_the_favorites()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var viewer = UserId.New();
        var blocked = UserId.New();
        await harness.AddIdentityUserAsync(blocked.Value, "محظور", "Blocked Author");

        Guid blockedPost = await harness.AddPublishedPostAsync(blocked.Value, isAnonymous: false);
        await harness.AddFavoriteAsync(viewer, blockedPost);
        await harness.BlockAuthorAsync(viewer, blocked);

        CommunityPostsController controller = harness.CreatePostsController(viewer);
        IReadOnlyList<CommunityPostResponse> items = harness.ReadItems(
            await controller.GetFavorites(pageSize: 20, cancellationToken: default));

        Assert.DoesNotContain(items, item => item.Id == blockedPost);
        Assert.Empty(items);
    }

    [Fact]
    public async Task Blocked_author_post_detail_is_not_found()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var viewer = UserId.New();
        var blocked = UserId.New();
        await harness.AddIdentityUserAsync(blocked.Value, "محظور", "Blocked Author");

        Guid blockedPost = await harness.AddPublishedPostAsync(blocked.Value, isAnonymous: false);
        await harness.BlockAuthorAsync(viewer, blocked);

        CommunityPostsController controller = harness.CreatePostsController(viewer);
        IActionResult result = await controller.GetPost(blockedPost, default);

        // The same not-found shape as an unknown or unpublished post.
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Unblocking_restores_the_visibility_of_the_authors_posts()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var viewer = UserId.New();
        var blocked = UserId.New();
        await harness.AddIdentityUserAsync(blocked.Value, "محظور", "Blocked Author");

        Guid blockedPost = await harness.AddPublishedPostAsync(blocked.Value, isAnonymous: false);
        await harness.BlockAuthorAsync(viewer, blocked);

        CommunityPostsController controller = harness.CreatePostsController(viewer);
        IReadOnlyList<CommunityPostResponse> hidden = harness.ReadItems(
            await controller.GetPosts(pageSize: 20, cancellationToken: default));
        Assert.DoesNotContain(hidden, item => item.Id == blockedPost);

        Result unblock = await harness.UnblockAuthorAsync(viewer, blocked);
        Assert.True(unblock.IsSuccess);

        IReadOnlyList<CommunityPostResponse> visible = harness.ReadItems(
            await controller.GetPosts(pageSize: 20, cancellationToken: default));
        Assert.Contains(visible, item => item.Id == blockedPost);
    }

    [Fact]
    public async Task Blocking_is_per_viewer_and_another_viewer_still_sees_the_post()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var viewer = UserId.New();
        var otherViewer = UserId.New();
        var blocked = UserId.New();
        await harness.AddIdentityUserAsync(blocked.Value, "محظور", "Blocked Author");

        Guid blockedPost = await harness.AddPublishedPostAsync(blocked.Value, isAnonymous: false);
        await harness.BlockAuthorAsync(viewer, blocked);

        CommunityPostsController other = harness.CreatePostsController(otherViewer);
        IReadOnlyList<CommunityPostResponse> items = harness.ReadItems(
            await other.GetPosts(pageSize: 20, cancellationToken: default));

        Assert.Contains(items, item => item.Id == blockedPost);
    }

    [Fact]
    public async Task Blocking_yourself_is_rejected_and_persists_nothing()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var viewer = UserId.New();
        await harness.AddIdentityUserAsync(viewer.Value, "نفسك", "Myself");

        Result result = await harness.BlockAuthorAsync(viewer, viewer);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityBlockErrors.Invalid.Code, result.Error.Code);
        Assert.Empty(harness.Context.CommunityAuthorBlocks);
    }

    [Fact]
    public async Task Blocking_an_unknown_author_is_rejected_and_persists_nothing()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var viewer = UserId.New();

        Result result = await harness.BlockAuthorAsync(viewer, UserId.New());

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityBlockErrors.NotFound.Code, result.Error.Code);
        Assert.Empty(harness.Context.CommunityAuthorBlocks);
    }

    [Fact]
    public async Task Blocking_the_same_author_twice_is_idempotent_and_keeps_a_single_row()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var viewer = UserId.New();
        var blocked = UserId.New();
        await harness.AddIdentityUserAsync(blocked.Value, "محظور", "Blocked Author");

        Assert.True((await harness.BlockAuthorAsync(viewer, blocked)).IsSuccess);
        Assert.True((await harness.BlockAuthorAsync(viewer, blocked)).IsSuccess);

        CommunityAuthorBlock stored = Assert.Single(harness.Context.CommunityAuthorBlocks);
        Assert.Equal(viewer.Value, stored.BlockerUserId.Value);
        Assert.Equal(blocked.Value, stored.BlockedAuthorUserId.Value);
    }

    [Fact]
    public async Task Unblocking_an_author_that_is_not_blocked_succeeds_without_a_row()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var viewer = UserId.New();
        var other = UserId.New();
        await harness.AddIdentityUserAsync(other.Value, "آخر", "Other Author");

        Result result = await harness.UnblockAuthorAsync(viewer, other);

        // The Chat convention: unblocking a missing relation still succeeds.
        Assert.True(result.IsSuccess);
        Assert.Empty(harness.Context.CommunityAuthorBlocks);
    }

    [Fact]
    public async Task Blocked_authors_list_resolves_the_display_names_with_one_batched_lookup()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var viewer = UserId.New();
        var blocked = UserId.New();
        var other = UserId.New();
        await harness.AddIdentityUserAsync(blocked.Value, "محظور", "Blocked Author");
        await harness.AddIdentityUserAsync(other.Value, "آخر", "Other Author");

        await harness.BlockAuthorAsync(viewer, blocked);
        await harness.BlockAuthorAsync(viewer, other);

        int before = harness.AuthorNameLookupCount;
        var result = await new GetBlockedCommunityAuthorsQueryHandler(harness.Context)
            .Handle(
                new GetBlockedCommunityAuthorsQuery(viewer),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(
            new[] { "Blocked Author", "Other Author" },
            result.Value.Select(item => item.DisplayName).OrderBy(name => name).ToArray());
        Assert.All(result.Value, item => Assert.Null(item.AvatarUrl));

        // The two blocked authors are compared as a set: the ids are
        // version-7 GUIDs whose timestamp prefix can tie, so a creation-order
        // expectation would only match by chance.
        Guid[] returned = result.Value.Select(item => item.AuthorId).ToArray();
        Assert.Equal(2, returned.Length);
        Assert.Contains(blocked.Value, returned);
        Assert.Contains(other.Value, returned);

        // One batched identity lookup for the whole page, never one per row.
        Assert.Equal(1, harness.AuthorNameLookupCount - before);
    }

    [Fact]
    public async Task Blocked_authors_list_is_per_viewer()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var viewer = UserId.New();
        var otherViewer = UserId.New();
        var blocked = UserId.New();
        await harness.AddIdentityUserAsync(blocked.Value, "محظور", "Blocked Author");

        await harness.BlockAuthorAsync(viewer, blocked);

        var handler = new GetBlockedCommunityAuthorsQueryHandler(harness.Context);
        var mine = await handler.Handle(
            new GetBlockedCommunityAuthorsQuery(viewer),
            CancellationToken.None);
        var theirs = await handler.Handle(
            new GetBlockedCommunityAuthorsQuery(otherViewer),
            CancellationToken.None);

        Assert.Single(mine.Value);
        Assert.Empty(theirs.Value);
    }

    [Fact]
    public async Task Comments_and_replies_of_a_blocked_author_are_not_hidden()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var viewer = UserId.New();
        var blocked = UserId.New();
        await harness.AddIdentityUserAsync(blocked.Value, "محظور", "Blocked Author");

        Guid postId = await harness.AddPublishedPostAsync(blocked.Value, isAnonymous: false);
        Guid commentId = await harness.AddCommentAsync(postId, blocked.Value);
        await harness.AddReplyAsync(commentId, blocked.Value);
        await harness.BlockAuthorAsync(viewer, blocked);

        CommentsController controller = harness.CreateCommentsController(viewer);
        IReadOnlyList<CommunityCommentResponse> comments = harness.ReadComments(
            await controller.GetPostComments(postId, cancellationToken: default));
        IReadOnlyList<CommunityReplyResponse> replies = harness.ReadReplies(
            await controller.GetCommentReplies(commentId, cancellationToken: default));

        // Blocking is hide-posts-only: comments and replies are unchanged.
        Assert.Single(comments);
        Assert.Single(replies);
        Assert.Equal(blocked.Value, comments[0].AuthorId);
        Assert.Equal(blocked.Value, replies[0].AuthorId);

        // And the comment write is not denied either.
        var created = await new CreateCommentCommandHandler(harness.Context).Handle(
            new CreateCommentCommand
            {
                PostId = postId,
                ContentArabic = "تعليق",
                ContentEnglish = "Comment",
                AuthorId = viewer.Value
            },
            CancellationToken.None);

        Assert.True(created.IsSuccess);
    }

    [Fact]
    public async Task An_anonymous_post_of_a_blocked_author_is_filtered_without_exposing_an_identity()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var viewer = UserId.New();
        var blocked = UserId.New();
        await harness.AddIdentityUserAsync(blocked.Value, "محظور", "Blocked Author");

        Guid anonymousPost = await harness.AddPublishedPostAsync(blocked.Value, isAnonymous: true);
        await harness.BlockAuthorAsync(viewer, blocked);

        CommunityPostsController controller = harness.CreatePostsController(viewer);
        IReadOnlyList<CommunityPostResponse> items = harness.ReadItems(
            await controller.GetPosts(pageSize: 20, cancellationToken: default));

        Assert.DoesNotContain(items, item => item.Id == anonymousPost);
        Assert.Empty(items);

        string json = harness.Serialize(items);
        Assert.DoesNotContain("Blocked Author", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(blocked.Value.ToString(), json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Block_endpoints_drive_the_controller_statuses_and_the_claim_identity()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var viewer = UserId.New();
        var blocked = UserId.New();
        await harness.AddIdentityUserAsync(blocked.Value, "محظور", "Blocked Author");

        CommunityBlocksController controller = harness.CreateBlocksController(viewer);

        // A valid block is 204 and the viewer comes from the authenticated
        // claim, never from the request body.
        IActionResult blockedResult = await controller.BlockAuthor(
            new BlockCommunityAuthorRequest(blocked.Value),
            default);

        Assert.IsType<NoContentResult>(blockedResult);
        CommunityAuthorBlock stored = Assert.Single(harness.Context.CommunityAuthorBlocks);
        Assert.Equal(viewer.Value, stored.BlockerUserId.Value);
        Assert.Equal(blocked.Value, stored.BlockedAuthorUserId.Value);

        // A self-block is the caller's own invalid request: 400 with the
        // Community.Block.Invalid code.
        IActionResult selfResult = await controller.BlockAuthor(
            new BlockCommunityAuthorRequest(viewer.Value),
            default);

        var selfBadRequest = Assert.IsType<ObjectResult>(selfResult);
        Assert.Equal(StatusCodes.Status400BadRequest, selfBadRequest.StatusCode);
        var selfProblem = Assert.IsType<ProblemDetails>(selfBadRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, selfProblem.Status);
        Assert.Equal(
            CommunityBlockErrors.Invalid.Code,
            selfProblem.Extensions["code"]?.ToString());

        // An unknown target author is the generic privacy 404: the body is
        // the generic detail, the code stays Community.Block.NotFound.
        IActionResult unknownResult = await controller.BlockAuthor(
            new BlockCommunityAuthorRequest(Guid.NewGuid()),
            default);

        var unknownStatus = Assert.IsType<ObjectResult>(unknownResult);
        Assert.Equal(StatusCodes.Status404NotFound, unknownStatus.StatusCode);
        var unknownProblem = Assert.IsType<ProblemDetails>(unknownStatus.Value);
        Assert.Equal(
            "The requested resource was not found.",
            unknownProblem.Detail);
        Assert.Equal(
            CommunityBlockErrors.NotFound.Code,
            unknownProblem.Extensions["code"]?.ToString());

        // Unblock is 204, also for an author that was never blocked.
        IActionResult unblockResult = await controller.UnblockAuthor(blocked.Value, default);
        Assert.IsType<NoContentResult>(unblockResult);
        Assert.Empty(harness.Context.CommunityAuthorBlocks);

        IActionResult unblockMissingResult = await controller.UnblockAuthor(
            Guid.NewGuid(),
            default);
        Assert.IsType<NoContentResult>(unblockMissingResult);

        // The list is a bare 200 list of the caller's own blocked authors.
        await harness.BlockAuthorAsync(viewer, blocked);
        IActionResult listResult = await controller.GetBlockedAuthors(
            page: 1,
            pageSize: 20,
            cancellationToken: default);

        var ok = Assert.IsType<OkObjectResult>(listResult);
        var listed = Assert.IsAssignableFrom<IReadOnlyList<BlockedCommunityAuthorResponse>>(ok.Value);
        Assert.Single(listed);
        Assert.Equal(blocked.Value, listed[0].AuthorId);
        Assert.Equal("Blocked Author", listed[0].DisplayName);
        Assert.Null(listed[0].AvatarUrl);
    }

    [Fact]
    public async Task Blocked_author_posts_are_excluded_before_the_feed_is_paged()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var viewer = UserId.New();
        var blocked = UserId.New();
        var other = UserId.New();
        await harness.AddIdentityUserAsync(blocked.Value, "محظور", "Blocked Author");
        await harness.AddIdentityUserAsync(other.Value, "آخر", "Other Author");

        DateTime start = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

        // Three visible posts, then the blocked author's three newest posts:
        // an unfiltered first page would be entirely the blocked author's.
        Guid firstVisible = await harness.AddPublishedPostAsync(other.Value, isAnonymous: false, start);
        Guid secondVisible = await harness.AddPublishedPostAsync(other.Value, isAnonymous: false, start.AddMinutes(1));
        Guid thirdVisible = await harness.AddPublishedPostAsync(other.Value, isAnonymous: false, start.AddMinutes(2));
        await harness.AddPublishedPostAsync(blocked.Value, isAnonymous: false, start.AddMinutes(3));
        await harness.AddPublishedPostAsync(blocked.Value, isAnonymous: false, start.AddMinutes(4));
        await harness.AddPublishedPostAsync(blocked.Value, isAnonymous: false, start.AddMinutes(5));

        await harness.BlockAuthorAsync(viewer, blocked);

        CommunityPostsController controller = harness.CreatePostsController(viewer);
        IReadOnlyList<CommunityPostResponse> items = harness.ReadItems(
            await controller.GetPosts(page: 1, pageSize: 2, cancellationToken: default));

        // A full page: the blocked posts were removed before the paging, so
        // the page is filled from the remaining visible posts. An unfiltered
        // first page would have held two of the blocked author's posts.
        Assert.Equal(2, items.Count);
        Assert.Equal(
            new[] { thirdVisible, secondVisible },
            items.Select(item => item.Id).ToArray());
        Assert.DoesNotContain(firstVisible, items.Select(item => item.Id));
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

/// <summary>
/// Shared harness for the Community author-projection tests: a SQLite Community
/// database that also carries the shared Identity <c>users</c> table the
/// batched author lookup reads, a counting context that fails an accidental
/// per-row identity lookup, and controllers wired to the real query handlers.
/// </summary>
internal sealed class CommunityAuthorProjectionHarness : IAsyncDisposable
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    private readonly SqliteConnection _connection;
    private readonly CommunityDbContext _inner;

    private CommunityAuthorProjectionHarness(SqliteConnection connection, CommunityDbContext inner)
    {
        _connection = connection;
        _inner = inner;
        Context = new CountingCommunityDbContext(inner);
    }

    public CountingCommunityDbContext Context { get; }

    public int AuthorNameLookupCount => Context.AuthorNameLookupCount;

    public IReadOnlyCollection<Guid> LastRequestedAuthorIds => Context.LastRequestedAuthorIds;

    public static async Task<CommunityAuthorProjectionHarness> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var inner = new CommunityDbContext(
            new DbContextOptionsBuilder<CommunityDbContext>().UseSqlite(connection).Options);
        await inner.Database.EnsureCreatedAsync();

        // The Identity users table shares the database in production; the
        // batched author lookup reads it directly.
        await inner.Database.ExecuteSqlRawAsync(
            "CREATE TABLE users (id TEXT NOT NULL PRIMARY KEY, " +
            "arabic_full_name TEXT NOT NULL, english_full_name TEXT NOT NULL);");

        return new CommunityAuthorProjectionHarness(connection, inner);
    }

    public Task AddIdentityUserAsync(Guid userId, string arabicName, string englishName) =>
        _inner.Database.ExecuteSqlRawAsync(
            "INSERT INTO users (id, arabic_full_name, english_full_name) VALUES ({0}, {1}, {2});",
            userId, arabicName, englishName);

    public async Task<Guid> AddPublishedPostAsync(
        Guid authorId,
        bool isAnonymous,
        DateTime? createdOnUtc = null)
    {
        DateTime stamp = createdOnUtc ?? DateTime.UtcNow;
        Post post = Post.Create(
            "عنوان", "Title", "محتوى", "Content", null, isAnonymous,
            new UserId(authorId), stamp);
        post.Publish(new UserId(authorId), stamp);
        _inner.Posts.Add(post);
        await _inner.SaveChangesAsync();
        return post.Id.Value;
    }

    public async Task<Guid> AddFavoriteAsync(UserId viewer, Guid postId)
    {
        _inner.Interactions.Add(CommunityInteraction.Create(
            CommunityInteractionTarget.Post,
            postId,
            viewer,
            CommunityInteractionKind.Favorite,
            DateTime.UtcNow));
        await _inner.SaveChangesAsync();
        return postId;
    }

    public async Task<Guid> AddCommentAsync(Guid postId, Guid authorId)
    {
        Comment comment = Comment.Create(
            new CommunityPostId(postId),
            new UserId(authorId),
            "تعليق",
            "Comment",
            DateTime.UtcNow);
        _inner.Comments.Add(comment);
        await _inner.SaveChangesAsync();
        return comment.Id.Value;
    }

    public async Task<Guid> AddReplyAsync(Guid commentId, Guid authorId)
    {
        Reply reply = Reply.Create(
            new CommunityCommentId(commentId),
            new UserId(authorId),
            "رد",
            "Reply",
            DateTime.UtcNow);
        _inner.Replies.Add(reply);
        await _inner.SaveChangesAsync();
        return reply.Id.Value;
    }

    public CommunityPostsController CreatePostsController(UserId actor) =>
        new(new CommunityAuthorProjectionSender(Context))
        {
            ControllerContext = ControllerContextFor(actor)
        };

    public CommentsController CreateCommentsController(UserId actor) =>
        new(new CommunityAuthorProjectionSender(Context))
        {
            ControllerContext = ControllerContextFor(actor)
        };

    public CommunityBlocksController CreateBlocksController(UserId actor) =>
        new(new CommunityAuthorProjectionSender(Context))
        {
            ControllerContext = ControllerContextFor(actor)
        };

    /// <summary>
    /// The viewer blocks an author through the real command handler, so the
    /// idempotency and validation rules are exercised as they are in the app.
    /// </summary>
    public async Task<Result> BlockAuthorAsync(UserId viewer, UserId target) =>
        await new BlockCommunityAuthorCommandHandler(Context).Handle(
            new BlockCommunityAuthorCommand(viewer, target),
            CancellationToken.None);

    public async Task<Result> UnblockAuthorAsync(UserId viewer, UserId target) =>
        await new UnblockCommunityAuthorCommandHandler(Context).Handle(
            new UnblockCommunityAuthorCommand(viewer, target),
            CancellationToken.None);

    public IReadOnlyList<CommunityPostResponse> ReadItems(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return ((System.Collections.IEnumerable)ok.Value!).Cast<CommunityPostResponse>().ToList();
    }

    public IReadOnlyList<CommunityCommentResponse> ReadComments(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return ((System.Collections.IEnumerable)ok.Value!).Cast<CommunityCommentResponse>().ToList();
    }

    public IReadOnlyList<CommunityReplyResponse> ReadReplies(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return ((System.Collections.IEnumerable)ok.Value!).Cast<CommunityReplyResponse>().ToList();
    }

    /// <summary>The wire shape the API returns (camelCase, like ASP.NET Core).</summary>
    public string Serialize(object value) => JsonSerializer.Serialize(value, WireJson);

    public async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static ControllerContext ControllerContextFor(UserId actor) =>
        new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [new Claim(JwtRegisteredClaimNames.Sub, actor.Value.ToString())],
                        "test"))
            }
        };
}

/// <summary>
/// Delegating <see cref="ICommunityDbContext"/> that records how many times the
/// identity enrichment is invoked, so an accidental per-row lookup fails the
/// test instead of passing silently.
/// </summary>
internal sealed class CountingCommunityDbContext(CommunityDbContext inner) : ICommunityDbContext
{
    public int AuthorNameLookupCount { get; private set; }

    public IReadOnlyCollection<Guid> LastRequestedAuthorIds { get; private set; } = [];

    public DbSet<Post> Posts => inner.Posts;

    public DbSet<Comment> Comments => inner.Comments;

    public DbSet<Reply> Replies => inner.Replies;

    public DbSet<CheckIn> CheckIns => inner.CheckIns;

    public DbSet<Rating> Ratings => inner.Ratings;

    public DbSet<CommunityInteraction> Interactions => inner.Interactions;

    public DbSet<CommunityImage> CommunityImages => inner.CommunityImages;

    public DbSet<CommunityCategory> Categories => inner.Categories;

    public DbSet<CommunityPostReport> CommunityPostReports => inner.CommunityPostReports;

    public DbSet<CommunityAuthorRestriction> CommunityAuthorRestrictions =>
        inner.CommunityAuthorRestrictions;

    public DbSet<CommunityAuthorBlock> CommunityAuthorBlocks => inner.CommunityAuthorBlocks;

    public Task<IDbContextTransaction?> BeginCommunityAuthorRestrictionTransactionAsync(
        UserId authorId,
        CancellationToken cancellationToken = default) =>
        inner.BeginCommunityAuthorRestrictionTransactionAsync(authorId, cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        inner.SaveChangesAsync(cancellationToken);

    public Task<IReadOnlyDictionary<Guid, CommunityAuthorName>> GetCommunityAuthorNamesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        AuthorNameLookupCount++;
        LastRequestedAuthorIds = userIds;
        return inner.GetCommunityAuthorNamesAsync(userIds, cancellationToken);
    }

    public Task<IReadOnlyList<Post>> GetRecommendedPostsAsync(
        UserId userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        inner.GetRecommendedPostsAsync(userId, page, pageSize, cancellationToken);
}

/// <summary>
/// Dispatches the Community reads to the real query handlers over the test
/// context, so the controller projection is exercised end to end.
/// </summary>
internal sealed class CommunityAuthorProjectionSender(ICommunityDbContext context) : ISender
{
    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        object? result = request switch
        {
            GetPostsQuery query => new GetPostsQueryHandler(context).Handle(query, cancellationToken),
            GetPostQuery query => new GetPostQueryHandler(context).Handle(query, cancellationToken),
            GetFavoritePostsQuery query =>
                new GetFavoritePostsQueryHandler(context).Handle(query, cancellationToken),
            GetCommunityRecommendationsQuery query =>
                new GetCommunityRecommendationsQueryHandler(context).Handle(query, cancellationToken),
            GetCommentsQuery query =>
                new GetCommentsQueryHandler(context).Handle(query, cancellationToken),
            GetCommentRepliesQuery query =>
                new GetCommentRepliesQueryHandler(context).Handle(query, cancellationToken),
            GetCommunityAuthorProfilesQuery query =>
                new GetCommunityAuthorProfilesQueryHandler(context).Handle(query, cancellationToken),
            GetCommunityThreadPostByPostIdQuery query =>
                new GetCommunityThreadPostByPostIdQueryHandler(context).Handle(query, cancellationToken),
            GetCommunityThreadPostByCommentIdQuery query =>
                new GetCommunityThreadPostByCommentIdQueryHandler(context).Handle(query, cancellationToken),
            BlockCommunityAuthorCommand query =>
                new BlockCommunityAuthorCommandHandler(context).Handle(query, cancellationToken),
            UnblockCommunityAuthorCommand query =>
                new UnblockCommunityAuthorCommandHandler(context).Handle(query, cancellationToken),
            GetBlockedCommunityAuthorsQuery query =>
                new GetBlockedCommunityAuthorsQueryHandler(context).Handle(query, cancellationToken),
            _ => throw new NotSupportedException($"Unexpected request {request.GetType().Name}.")
        };

        return (Task<TResponse>)result!;
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest =>
        throw new NotSupportedException();

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(
        object request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
