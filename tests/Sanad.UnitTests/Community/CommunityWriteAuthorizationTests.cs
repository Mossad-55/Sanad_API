using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Comments;
using Sanad.Modules.Community.Application.Posts;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.Modules.Community.Domain.Interactions;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Infrastructure.Persistence;

namespace Sanad.UnitTests.Community;

public sealed class CommunityWriteAuthorizationTests
{
    [Fact]
    public async Task Like_IsPerUserAndIdempotent()
    {
        await using var db = CreateDb();
        var post = CreatePost();
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        UserId userId = UserId.New();
        var handler = new LikePostCommandHandler(db);
        var command = new LikePostCommand { PostId = post.Id.Value, UserId = userId.Value };

        Assert.True((await handler.Handle(command, default)).Value);
        Assert.True((await handler.Handle(command, default)).Value);
        Assert.Equal(1, post.LikesCount);
        Assert.Equal(1, await db.Interactions.CountAsync());
    }

    [Fact]
    public async Task Unlike_DoesNotRemoveAnotherUsersLike()
    {
        await using var db = CreateDb();
        var post = CreatePost();
        db.Posts.Add(post);
        db.Interactions.Add(CommunityInteraction.Create(
            CommunityInteractionTarget.Post, post.Id.Value, UserId.New(),
            CommunityInteractionKind.Like, DateTime.UtcNow));
        post.IncrementLikes();
        await db.SaveChangesAsync();

        Assert.True((await new UnlikePostCommandHandler(db).Handle(
            new UnlikePostCommand(post.Id.Value, UserId.New().Value), default)).Value);

        Assert.Equal(1, post.LikesCount);
        Assert.Equal(1, await db.Interactions.CountAsync());
    }

    [Fact]
    public async Task UpdateComment_ReturnsNotFoundForNonOwnerAndDoesNotMutate()
    {
        await using var db = CreateDb();
        var post = CreatePost();
        UserId author = UserId.New();
        Comment comment = Comment.Create(post.Id, author, "Original", "Original", DateTime.UtcNow);
        db.Posts.Add(post);
        db.Comments.Add(comment);
        await db.SaveChangesAsync();

        var result = await new UpdateCommentCommandHandler(db).Handle(new UpdateCommentCommand
        {
            CommentId = comment.Id.Value,
            AuthorId = UserId.New().Value,
            ContentArabic = "Changed",
            ContentEnglish = "Changed"
        }, default);

        Assert.Null(result.Value);
        Assert.Equal("Original", (await db.Comments.AsNoTracking().SingleAsync()).ContentEnglish);
    }

    private static Post CreatePost() => Post.Create(
        "Title Arabic", "Title", "Content Arabic", "Content", null, false,
        UserId.New(), DateTime.UtcNow);

    private static CommunityDbContext CreateDb() => new(new DbContextOptionsBuilder<CommunityDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);
}
