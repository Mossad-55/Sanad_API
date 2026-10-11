using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Ratings;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Infrastructure.Persistence;

namespace Sanad.UnitTests.Community;

public sealed class CommunityRatingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task AddRating_ValidValue_PersistsRating(int value)
    {
        await using var db = CreateDb();
        var user = UserId.New();
        var post = CreatePublishedPost();
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var result = await new AddRatingCommandHandler(db).Handle(new AddRatingCommand
        {
            PostId = post.Id.Value,
            UserId = user.Value,
            Value = value
        }, default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        var rating = await db.Ratings.SingleAsync();
        Assert.Equal(post.Id, rating.PostId);
        Assert.Equal(new UserId(user.Value), rating.UserId);
        Assert.Equal(value.ToString(), rating.RatingValue);
    }

    [Fact]
    public async Task AddRating_ValidValue_TranslatesOnRelationalStore()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new CommunityDbContext(
            new DbContextOptionsBuilder<CommunityDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var user = UserId.New();
        var post = CreatePublishedPost();
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var result = await new AddRatingCommandHandler(db).Handle(new AddRatingCommand
        {
            PostId = post.Id.Value,
            UserId = user.Value,
            Value = 4
        }, default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task AddRating_OutOfRange_ReturnsInvalidErrorAndStoresNothing(int value)
    {
        await using var db = CreateDb();
        var post = CreatePublishedPost();
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var result = await new AddRatingCommandHandler(db).Handle(new AddRatingCommand
        {
            PostId = post.Id.Value,
            UserId = UserId.New().Value,
            Value = value
        }, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Community.Rating.Invalid", result.Error.Code);
        Assert.Empty(db.Ratings);
    }

    [Fact]
    public async Task AddRating_RepeatedSubmission_UpdatesExistingRow()
    {
        await using var db = CreateDb();
        var user = UserId.New();
        var post = CreatePublishedPost();
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var handler = new AddRatingCommandHandler(db);
        var first = await handler.Handle(new AddRatingCommand
        {
            PostId = post.Id.Value,
            UserId = user.Value,
            Value = 4
        }, default);
        var second = await handler.Handle(new AddRatingCommand
        {
            PostId = post.Id.Value,
            UserId = user.Value,
            Value = 2
        }, default);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        var rating = await db.Ratings.SingleAsync();
        Assert.Equal("2", rating.RatingValue);
    }

    [Fact]
    public async Task AddRating_UnpublishedOrUnknownPost_ReturnsFalse()
    {
        await using var db = CreateDb();
        var pending = Post.Create(
            "عنوان", "Title", "محتوى", "Content", null, false, UserId.New(), DateTime.UtcNow);
        db.Posts.Add(pending);
        await db.SaveChangesAsync();

        var handler = new AddRatingCommandHandler(db);
        var unpublished = await handler.Handle(new AddRatingCommand
        {
            PostId = pending.Id.Value,
            UserId = UserId.New().Value,
            Value = 5
        }, default);
        var unknown = await handler.Handle(new AddRatingCommand
        {
            PostId = Guid.NewGuid(),
            UserId = UserId.New().Value,
            Value = 5
        }, default);

        Assert.True(unpublished.IsSuccess);
        Assert.False(unpublished.Value);
        Assert.True(unknown.IsSuccess);
        Assert.False(unknown.Value);
        Assert.Empty(db.Ratings);
    }

    private static Post CreatePublishedPost()
    {
        var post = Post.Create(
            "عنوان", "Title", "محتوى", "Content", null, false, UserId.New(), DateTime.UtcNow);
        post.Publish(UserId.New(), DateTime.UtcNow);
        return post;
    }

    private static CommunityDbContext CreateDb() => new(
        new DbContextOptionsBuilder<CommunityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
