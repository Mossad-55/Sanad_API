using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.CheckIns;
using Sanad.Modules.Community.Application.Posts;
using Sanad.Modules.Community.Application.Ratings;
using Sanad.Modules.Community.Domain.CheckIns;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Infrastructure.Persistence;

namespace Sanad.UnitTests.Community;

public sealed class CommunityModerationAndCheckInTests
{
    [Fact]
    public void Post_Publish_RequiresPendingReviewAndRecordsModerator()
    {
        var moderator = UserId.New();
        var post = CreatePost();

        post.Publish(moderator, DateTime.UtcNow);

        Assert.Equal(PostStatus.Published, post.Status);
        Assert.Equal(moderator.Value.ToString(), post.PublishedBy);
        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(
            () => post.Publish(moderator, DateTime.UtcNow));
    }

    [Fact]
    public void Post_Reject_RequiresPendingReview()
    {
        var post = CreatePost();

        post.Reject(DateTime.UtcNow);

        Assert.Equal(PostStatus.Rejected, post.Status);
        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(
            () => post.Reject(DateTime.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Not/A/Real/TimeZone")]
    public async Task CheckIn_InvalidIdentityOrTimezone_ReturnsFailure(string timeZoneId)
    {
        await using var db = CreateDb();
        var result = await new AddCheckInCommandHandler(db).Handle(new AddCheckInCommand
        {
            PostId = Guid.NewGuid(),
            UserId = timeZoneId.Length == 0 ? Guid.Empty : Guid.NewGuid(),
            Answer = true,
            TimeZoneId = timeZoneId,
            LocalDate = DateOnly.FromDateTime(DateTime.UtcNow),
            AnsweredAtLocalTime = new TimeOnly(9, 0)
        }, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Community.CheckIn.Invalid", result.Error.Code);
        Assert.Empty(db.CheckIns);
    }

    [Fact]
    public async Task CheckIn_UsesTypedPostIdAndPersistsAuthenticatedUser()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        var user = UserId.New();
        var post = CreatePost(author);
        post.Publish(UserId.New(), DateTime.UtcNow);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var command = new AddCheckInCommand
        {
            PostId = post.Id.Value,
            UserId = user.Value,
            Answer = true,
            TimeZoneId = "UTC",
            LocalDate = new DateOnly(2026, 10, 5),
            AnsweredAtLocalTime = new TimeOnly(9, 0)
        };
        var handler = new AddCheckInCommandHandler(db);
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        var checkIn = await db.CheckIns.SingleAsync();
        Assert.Equal(post.Id, checkIn.PostId);
        Assert.Equal(new UserId(user.Value), checkIn.UserId);

    }

    [Fact]
    public async Task Rating_UsesTypedPostIdAndPersistsAuthenticatedUser()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        var user = UserId.New();
        var post = CreatePost(author);
        post.Publish(UserId.New(), DateTime.UtcNow);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var result = await new AddRatingCommandHandler(db).Handle(new AddRatingCommand
        {
            PostId = post.Id.Value,
            UserId = user.Value,
            RatingValue = "5"
        }, default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        var rating = await db.Ratings.SingleAsync();
        Assert.Equal(post.Id, rating.PostId);
        Assert.Equal(new UserId(user.Value), rating.UserId);
        Assert.Equal("5", rating.RatingValue);
    }

    [Fact]
    public async Task GetPosts_ClampsPageBoundsAndReturnsRequestedSlice()
    {
        await using var db = CreateDb();
        db.Posts.AddRange(CreatePost(), CreatePost(), CreatePost());
        await db.SaveChangesAsync();

        var page = await new GetPostsQueryHandler(db).Handle(
            new GetPostsQuery(PostStatus.PendingReview, Page: 0, PageSize: 0), default);

        Assert.True(page.IsSuccess);
        Assert.Single(page.Value);

        var secondPage = await new GetPostsQueryHandler(db).Handle(
            new GetPostsQuery(PostStatus.PendingReview, Page: 2, PageSize: 2), default);
        Assert.True(secondPage.IsSuccess);
        Assert.Single(secondPage.Value);
    }

    private static Post CreatePost(UserId? author = null) => Post.Create(
        "Title Arabic", "Title English", "Content Arabic", "Content English", null,
        false, author ?? UserId.New(), DateTime.UtcNow);

    private static CommunityDbContext CreateDb() => new(
        new DbContextOptionsBuilder<CommunityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
