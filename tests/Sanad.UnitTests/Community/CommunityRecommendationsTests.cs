using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Posts;
using Sanad.Modules.Community.Domain.CheckIns;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.Modules.Community.Domain.Interactions;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Domain.Ratings;
using Sanad.Modules.Community.Infrastructure.Persistence;

namespace Sanad.UnitTests.Community;

public sealed class CommunityRecommendationsTests
{
    [Fact]
    public async Task Recommendations_ReturnOnlyPublishedPosts()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        db.Posts.Add(PublishedPost(author, 1));
        db.Posts.Add(PendingPost(author, 2));
        db.Posts.Add(RejectedPost(author, 3));
        await db.SaveChangesAsync();

        var result = await RecommendAsync(db, UserId.New(), 1, 10);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value);
        Assert.Equal(PostStatus.Published, item.Status);
    }

    [Fact]
    public async Task Recommendations_PrioritizeAffinityAuthorsOverRecency()
    {
        await using var db = CreateDb();
        var authorA = UserId.New();
        var authorB = UserId.New();
        var user = UserId.New();
        Post interacted = PublishedPost(authorA, 1);
        Post affinity = PublishedPost(authorA, 3);
        Post newest = PublishedPost(authorB, 4);
        db.Posts.AddRange(interacted, affinity, newest);
        Like(db, interacted, user);
        await db.SaveChangesAsync();

        var result = await RecommendAsync(db, user, 1, 10);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [affinity.Id.Value, newest.Id.Value],
            result.Value.Select(p => p.Id.Value));
    }

    [Fact]
    public async Task Recommendations_FallBackToNewest_WhenNoInteractions()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post first = PublishedPost(author, 1);
        Post second = PublishedPost(author, 2);
        Post third = PublishedPost(author, 3);
        db.Posts.AddRange(first, second, third);
        await db.SaveChangesAsync();

        var result = await RecommendAsync(db, UserId.New(), 1, 10);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [third.Id.Value, second.Id.Value, first.Id.Value],
            result.Value.Select(p => p.Id.Value));
    }

    [Fact]
    public async Task Recommendations_DifferPerUser()
    {
        await using var db = CreateDb();
        var authorA = UserId.New();
        var authorB = UserId.New();
        var engaged = UserId.New();
        var fresh = UserId.New();
        Post interacted = PublishedPost(authorA, 1);
        Post affinity = PublishedPost(authorA, 2);
        Post newest = PublishedPost(authorB, 3);
        db.Posts.AddRange(interacted, affinity, newest);
        Like(db, interacted, engaged);
        await db.SaveChangesAsync();

        var forEngaged = await RecommendAsync(db, engaged, 1, 10);
        var forFresh = await RecommendAsync(db, fresh, 1, 10);

        Assert.True(forEngaged.IsSuccess);
        Assert.True(forFresh.IsSuccess);
        Assert.Equal(affinity.Id.Value, forEngaged.Value.First().Id.Value);
        Assert.Equal(newest.Id.Value, forFresh.Value.First().Id.Value);
    }

    [Fact]
    public async Task Recommendations_ExcludeEveryInteractionKind()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        var user = UserId.New();
        Post liked = PublishedPost(author, 1);
        Post favorited = PublishedPost(author, 2);
        Post commented = PublishedPost(author, 3);
        Post rated = PublishedPost(author, 4);
        Post checkedIn = PublishedPost(author, 5);
        Post untouched = PublishedPost(author, 6);
        db.Posts.AddRange(liked, favorited, commented, rated, checkedIn, untouched);
        Like(db, liked, user);
        Favorite(db, favorited, user);
        db.Comments.Add(Comment.Create(commented.Id, user, "رأي", "Opinion", Utc(7)));
        commented.IncrementComments();
        db.Ratings.Add(Rating.Create(rated.Id, user, 5, Utc(7)));
        db.CheckIns.Add(CheckIn.Create(
            checkedIn.Id, user, true, "Africa/Cairo",
            DateOnly.FromDateTime(Utc(7)), new TimeOnly(14, 30), Utc(7)));
        await db.SaveChangesAsync();

        var result = await RecommendAsync(db, user, 1, 10);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value);
        Assert.Equal(untouched.Id.Value, item.Id.Value);
    }

    [Fact]
    public async Task Recommendations_ExcludeOwnPostsFromAffinity()
    {
        await using var db = CreateDb();
        var user = UserId.New();
        var other = UserId.New();
        Post ownNewest = PublishedPost(user, 5);
        Post ownOlder = PublishedPost(user, 1);
        Post otherPost = PublishedPost(other, 4);
        db.Posts.AddRange(ownNewest, ownOlder, otherPost);
        Like(db, ownNewest, user);
        await db.SaveChangesAsync();

        var result = await RecommendAsync(db, user, 1, 10);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [otherPost.Id.Value, ownOlder.Id.Value],
            result.Value.Select(p => p.Id.Value));
    }

    [Fact]
    public async Task Recommendations_PageDeterministically()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        db.Posts.AddRange(
            PublishedPost(author, 1),
            PublishedPost(author, 2),
            PublishedPost(author, 3));
        await db.SaveChangesAsync();
        var user = UserId.New();

        var first = await RecommendAsync(db, user, 1, 2);
        var second = await RecommendAsync(db, user, 2, 2);
        var repeat = await RecommendAsync(db, user, 1, 2);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, first.Value.Count);
        Assert.Single(second.Value);
        Assert.Empty(first.Value.Select(p => p.Id.Value).Intersect(second.Value.Select(p => p.Id.Value)));
        Assert.Equal(
            first.Value.Select(p => p.Id.Value),
            repeat.Value.Select(p => p.Id.Value));
    }

    private static async Task<Sanad.BuildingBlocks.Application.Results.Result<IReadOnlyList<Post>>> RecommendAsync(
        CommunityDbContext db, UserId user, int page, int pageSize) =>
        await new GetCommunityRecommendationsQueryHandler(db).Handle(
            new GetCommunityRecommendationsQuery(user.Value, page, pageSize), default);

    private static Post PublishedPost(UserId author, int day)
    {
        Post post = CreatePost(author, day);
        post.Publish(UserId.New(), Utc(day));
        return post;
    }

    private static Post PendingPost(UserId author, int day) =>
        CreatePost(author, day);

    private static Post RejectedPost(UserId author, int day)
    {
        Post post = CreatePost(author, day);
        post.Reject(Utc(day));
        return post;
    }

    private static Post CreatePost(UserId author, int day) => Post.Create(
        "Title Arabic", "Title", "Content Arabic", "Content", null, false,
        author, Utc(day));

    private static void Like(CommunityDbContext db, Post post, UserId user)
    {
        db.Interactions.Add(CommunityInteraction.Create(
            CommunityInteractionTarget.Post, post.Id.Value, user,
            CommunityInteractionKind.Like, Utc(7)));
        post.IncrementLikes();
    }

    private static void Favorite(CommunityDbContext db, Post post, UserId user)
    {
        db.Interactions.Add(CommunityInteraction.Create(
            CommunityInteractionTarget.Post, post.Id.Value, user,
            CommunityInteractionKind.Favorite, Utc(7)));
        post.IncrementFavorites();
    }

    private static DateTime Utc(int day) => new(2026, 10, day, 0, 0, 0, DateTimeKind.Utc);

    private static CommunityDbContext CreateDb() => new(
        new DbContextOptionsBuilder<CommunityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
