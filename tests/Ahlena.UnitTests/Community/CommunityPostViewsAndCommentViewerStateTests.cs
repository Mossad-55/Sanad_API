using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Application.Comments;
using Ahlena.Modules.Community.Application.Posts;
using Ahlena.Modules.Community.Domain.Comments;

namespace Ahlena.UnitTests.Community;

/// <summary>
/// M1–M3 mobile-report facts over the real Community query/command handlers and
/// controllers: lifetime-unique post views (author excluded), <c>viewsCount</c>
/// and <c>authorId</c> on every post surface, and the comment/reply viewer
/// state that mirrors the actual mutation handlers.
/// </summary>
public sealed class CommunityPostViewsAndCommentViewerStateTests
{
    [Fact]
    public async Task First_view_counts_once_and_a_repeat_returns_the_same_count_without_a_second_row()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var author = UserId.New();
        var viewer = UserId.New();
        Guid postId = await harness.AddPublishedPostAsync(author.Value, isAnonymous: false);

        Result<CommunityPostViewResult?> first = await harness.RecordViewAsync(postId, viewer);
        Result<CommunityPostViewResult?> repeat = await harness.RecordViewAsync(postId, viewer);
        Result<CommunityPostViewResult?> secondViewer =
            await harness.RecordViewAsync(postId, UserId.New());

        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Value!.ViewsCount);
        Assert.Equal(postId, first.Value.PostId);
        Assert.Equal(1, repeat.Value!.ViewsCount);
        Assert.Equal(2, secondViewer.Value!.ViewsCount);
        Assert.Equal(2, await harness.StoredViewCountAsync(postId));
        Assert.Equal(2, await harness.ViewerRowCountAsync(postId));
    }

    [Fact]
    public async Task The_post_author_never_increments_the_counter_and_stores_no_viewer_row()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var author = UserId.New();
        Guid postId = await harness.AddPublishedPostAsync(author.Value, isAnonymous: true);

        Result<CommunityPostViewResult?> result = await harness.RecordViewAsync(postId, author);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.ViewsCount);
        Assert.Equal(0, await harness.StoredViewCountAsync(postId));
        Assert.Equal(0, await harness.ViewerRowCountAsync(postId));
    }

    [Fact]
    public async Task Invisible_posts_answer_not_found_and_store_nothing()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var author = UserId.New();
        var viewer = UserId.New();
        Guid pending = await harness.AddPendingPostAsync(author.Value);
        Guid deleted = await harness.AddDeletedPostAsync(author.Value);
        Guid blockedAuthorPost = await harness.AddPublishedPostAsync(author.Value, isAnonymous: false);
        await harness.AddIdentityUserAsync(author.Value, "مؤلف", "Blocked Author");
        Result block = await harness.BlockAuthorAsync(viewer, author);
        Assert.True(block.IsSuccess);

        Result<CommunityPostViewResult?> unknown = await harness.RecordViewAsync(Guid.NewGuid(), viewer);
        Result<CommunityPostViewResult?> pendingResult = await harness.RecordViewAsync(pending, viewer);
        Result<CommunityPostViewResult?> deletedResult = await harness.RecordViewAsync(deleted, viewer);
        Result<CommunityPostViewResult?> blockedResult =
            await harness.RecordViewAsync(blockedAuthorPost, viewer);

        Assert.Null(unknown.Value);
        Assert.Null(pendingResult.Value);
        Assert.Null(deletedResult.Value);
        Assert.Null(blockedResult.Value);
        Assert.Equal(0, await harness.StoredViewCountAsync(pending));
        Assert.Equal(0, await harness.StoredViewCountAsync(deleted));
        Assert.Equal(0, await harness.StoredViewCountAsync(blockedAuthorPost));
        Assert.Equal(0, await harness.ViewerRowCountAsync(blockedAuthorPost));
    }

    [Fact]
    public async Task Every_post_read_surface_carries_the_stored_views_count_and_author_id()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var namedAuthor = UserId.New();
        var anonymousAuthor = UserId.New();
        var viewer = UserId.New();
        var otherReader = UserId.New();

        Guid namedPost = await harness.AddPublishedPostAsync(namedAuthor.Value, isAnonymous: false);
        Guid anonymousPost = await harness.AddPublishedPostAsync(anonymousAuthor.Value, isAnonymous: true);
        await harness.RecordViewAsync(namedPost, viewer);
        await harness.RecordViewAsync(anonymousPost, viewer);
        await harness.AddFavoriteAsync(viewer, namedPost);

        CommunityPostsController viewerController = harness.CreatePostsController(viewer);
        IReadOnlyList<CommunityPostResponse> feed = harness.ReadItems(
            await viewerController.GetPosts(page: 1, pageSize: 20, cancellationToken: default));
        IReadOnlyList<CommunityPostResponse> favorites = harness.ReadItems(
            await viewerController.GetFavorites(cancellationToken: default));

        CommunityPostResponse named = Assert.Single(feed, item => item.Id == namedPost);
        CommunityPostResponse anonymous = Assert.Single(feed, item => item.Id == anonymousPost);
        Assert.Equal(1, named.ViewsCount);
        Assert.Equal(namedAuthor.Value, named.AuthorId);
        Assert.Equal(1, anonymous.ViewsCount);
        Assert.Null(anonymous.AuthorId);
        Assert.Equal(1, Assert.Single(favorites).ViewsCount);

        // Detail, read by the anonymous post's own author: viewsCount is the
        // stored counter and authorId stays null even for the author.
        CommunityPostsController authorController = harness.CreatePostsController(anonymousAuthor);
        var detail = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(
            await authorController.GetPost(anonymousPost, cancellationToken: default));
        var detailBody = Assert.IsType<CommunityPostResponse>(detail.Value);
        Assert.Equal(1, detailBody.ViewsCount);
        Assert.Null(detailBody.AuthorId);
        Assert.True(detailBody.IsOwn);
        string detailJson = harness.Serialize(detailBody);
        Assert.Contains("\"authorId\":null", detailJson);
        Assert.DoesNotContain(anonymousAuthor.Value.ToString(), detailJson, StringComparison.OrdinalIgnoreCase);

        // Recommendations are read by a caller with no interactions, so both
        // posts remain eligible; each keeps its stored count.
        CommunityPostsController otherController = harness.CreatePostsController(otherReader);
        IReadOnlyList<CommunityPostResponse> recommendations = harness.ReadItems(
            await otherController.GetRecommendations(cancellationToken: default));
        Assert.Equal(1, Assert.Single(recommendations, item => item.Id == namedPost).ViewsCount);
        Assert.Equal(1, Assert.Single(recommendations, item => item.Id == anonymousPost).ViewsCount);
    }

    [Fact]
    public async Task Comment_capabilities_match_the_real_update_and_delete_handlers()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var author = UserId.New();
        var visitor = UserId.New();
        Guid postId = await harness.AddPublishedPostAsync(author.Value, isAnonymous: false);
        Guid ownComment = await harness.AddCommentAsync(postId, visitor.Value);
        Guid answeredComment = await harness.AddCommentAsync(postId, author.Value);
        await harness.AddReplyAsync(answeredComment, visitor.Value);
        await harness.AddCommentLikeAsync(ownComment, visitor);

        CommentsController controller = harness.CreateCommentsController(visitor);
        IReadOnlyList<CommunityCommentResponse> comments = harness.ReadComments(
            await controller.GetPostComments(postId, cancellationToken: default));

        CommunityCommentResponse own = Assert.Single(comments, item => item.Id == ownComment);
        Assert.True(own.IsOwn);
        Assert.True(own.CanEdit);
        Assert.True(own.CanDelete);
        Assert.True(own.IsLiked);

        CommunityCommentResponse answered = Assert.Single(comments, item => item.Id == answeredComment);
        Assert.False(answered.IsOwn);
        Assert.False(answered.CanEdit);
        Assert.False(answered.CanDelete);
        Assert.False(answered.IsLiked);

        // Parity with the REAL handlers: every promised capability succeeds,
        // and every refused one is refused by the handler too.
        Result<Comment?> ownEdit = await new UpdateCommentCommandHandler(harness.Context).Handle(
            new UpdateCommentCommand
            {
                CommentId = ownComment,
                AuthorId = visitor.Value,
                ContentArabic = "محدث",
                ContentEnglish = "Updated"
            },
            CancellationToken.None);
        Result<Comment?> foreignEdit = await new UpdateCommentCommandHandler(harness.Context).Handle(
            new UpdateCommentCommand
            {
                CommentId = answeredComment,
                AuthorId = visitor.Value,
                ContentArabic = "محدث",
                ContentEnglish = "Updated"
            },
            CancellationToken.None);
        Assert.NotNull(ownEdit.Value);
        Assert.Null(foreignEdit.Value);

        Result<CommentDeleteResult> answeredDelete = await new DeleteCommentCommandHandler(harness.Context).Handle(
            new DeleteCommentCommand(answeredComment, author.Value),
            CancellationToken.None);
        Assert.Equal("CommentHasReplies", answeredDelete.Value.Error);

        Result<CommentDeleteResult> ownDelete = await new DeleteCommentCommandHandler(harness.Context).Handle(
            new DeleteCommentCommand(ownComment, visitor.Value),
            CancellationToken.None);
        Assert.True(ownDelete.Value.Success);
    }

    [Fact]
    public async Task Anonymous_comment_reveals_no_identity_and_only_its_author_sees_own_capabilities()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var poster = UserId.New();
        var visitor = UserId.New();
        Guid postId = await harness.AddPublishedPostAsync(poster.Value, isAnonymous: true);
        Guid anonymousComment = await harness.AddCommentAsync(postId, poster.Value);
        await harness.AddCommentAsync(postId, visitor.Value);

        CommentsController visitorController = harness.CreateCommentsController(visitor);
        IReadOnlyList<CommunityCommentResponse> asVisitor = harness.ReadComments(
            await visitorController.GetPostComments(postId, cancellationToken: default));
        CommunityCommentResponse seenByOther = Assert.Single(asVisitor, item => item.Id == anonymousComment);
        Assert.Null(seenByOther.AuthorId);
        Assert.True(seenByOther.IsAnonymous);
        Assert.False(seenByOther.IsOwn);
        Assert.False(seenByOther.CanEdit);
        Assert.False(seenByOther.CanDelete);
        string visitorJson = harness.Serialize(seenByOther);
        Assert.Contains("\"authorId\":null", visitorJson);
        Assert.DoesNotContain(poster.Value.ToString(), visitorJson, StringComparison.OrdinalIgnoreCase);

        CommentsController authorController = harness.CreateCommentsController(poster);
        IReadOnlyList<CommunityCommentResponse> asAuthor = harness.ReadComments(
            await authorController.GetPostComments(postId, cancellationToken: default));
        CommunityCommentResponse seenByAuthor = Assert.Single(asAuthor, item => item.Id == anonymousComment);
        Assert.Null(seenByAuthor.AuthorId);
        Assert.True(seenByAuthor.IsOwn);
        Assert.True(seenByAuthor.CanEdit);
        Assert.True(seenByAuthor.CanDelete);
        Assert.DoesNotContain(
            poster.Value.ToString(),
            harness.Serialize(seenByAuthor),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reply_viewer_state_carries_is_own_and_like_state_without_unavailable_capabilities()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var poster = UserId.New();
        var visitor = UserId.New();
        Guid postId = await harness.AddPublishedPostAsync(poster.Value, isAnonymous: true);
        Guid commentId = await harness.AddCommentAsync(postId, visitor.Value);
        Guid visitorReply = await harness.AddReplyAsync(commentId, visitor.Value);
        Guid anonymousAuthorReply = await harness.AddReplyAsync(commentId, poster.Value);
        await harness.AddReplyLikeAsync(visitorReply, visitor);

        CommentsController controller = harness.CreateCommentsController(visitor);
        IReadOnlyList<CommunityReplyResponse> replies = harness.ReadReplies(
            await controller.GetCommentReplies(commentId, cancellationToken: default));

        CommunityReplyResponse own = Assert.Single(replies, item => item.Id == visitorReply);
        Assert.True(own.IsOwn);
        Assert.True(own.IsLiked);
        // No reply edit/delete route exists anywhere in the API: a true here
        // could never precede anything but a 404/405.
        Assert.False(own.CanEdit);
        Assert.False(own.CanDelete);

        CommunityReplyResponse anonymous = Assert.Single(replies, item => item.Id == anonymousAuthorReply);
        Assert.False(anonymous.IsOwn);
        Assert.False(anonymous.IsLiked);
        Assert.False(anonymous.CanEdit);
        Assert.False(anonymous.CanDelete);
        Assert.True(anonymous.IsAnonymous);
        Assert.Null(anonymous.AuthorId);
        Assert.DoesNotContain(
            poster.Value.ToString(),
            harness.Serialize(anonymous),
            StringComparison.OrdinalIgnoreCase);
    }
}
