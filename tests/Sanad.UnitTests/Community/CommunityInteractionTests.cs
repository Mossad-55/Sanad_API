using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Application.Comments;
using Sanad.Modules.Community.Application.Posts;
using Sanad.Modules.Community.Domain.Categories;
using Sanad.Modules.Community.Domain.CheckIns;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.Modules.Community.Domain.Interactions;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Domain.Ratings;
using Sanad.Modules.Community.Domain.Uploads;

namespace Sanad.UnitTests.Community;

public sealed class CommunityInteractionTests
{
    [Fact]
    public void Create_RejectsFavoriteOnComment()
    {
        Assert.Throws<ArgumentException>(() => CommunityInteraction.Create(
            CommunityInteractionTarget.Comment,
            Guid.NewGuid(),
            UserId.New(),
            CommunityInteractionKind.Favorite,
            DateTime.UtcNow));
    }

    [Fact]
    public void Create_RequiresActor()
    {
        Assert.Throws<ArgumentException>(() => CommunityInteraction.Create(
            CommunityInteractionTarget.Post,
            Guid.NewGuid(),
            UserId.Empty,
            CommunityInteractionKind.Like,
            DateTime.UtcNow));
    }

    [Fact]
    public async Task Comment_list_projects_the_author_display_name()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var author = UserId.New();
        await harness.AddIdentityUserAsync(author.Value, "الكاتب", "Named Author");
        Guid postId = await harness.AddPublishedPostAsync(author.Value, isAnonymous: false);
        await harness.AddCommentAsync(postId, author.Value);

        CommentsController controller = harness.CreateCommentsController(UserId.New());
        IReadOnlyList<CommunityCommentResponse> comments = harness.ReadComments(
            await controller.GetPostComments(postId, cancellationToken: default));

        CommunityCommentResponse comment = Assert.Single(comments);
        Assert.Equal(author.Value, comment.AuthorId);
        Assert.False(comment.IsAnonymous);
        Assert.Equal("Named Author", comment.DisplayName);
        Assert.Null(comment.AvatarUrl);
    }

    [Fact]
    public async Task Comment_written_by_the_author_of_an_anonymous_post_carries_no_identity()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var poster = UserId.New();
        var visitor = UserId.New();
        await harness.AddIdentityUserAsync(poster.Value, "المؤلف", "Post Author");
        await harness.AddIdentityUserAsync(visitor.Value, "زائر", "Visitor");

        Guid postId = await harness.AddPublishedPostAsync(poster.Value, isAnonymous: true);
        await harness.AddCommentAsync(postId, poster.Value);
        await harness.AddCommentAsync(postId, visitor.Value);

        CommentsController controller = harness.CreateCommentsController(UserId.New());
        IReadOnlyList<CommunityCommentResponse> comments = harness.ReadComments(
            await controller.GetPostComments(postId, cancellationToken: default));

        CommunityCommentResponse own = Assert.Single(comments, item => item.AuthorId is null);
        Assert.True(own.IsAnonymous);
        Assert.Null(own.DisplayName);
        Assert.Null(own.AvatarUrl);
        string ownJson = harness.Serialize(own);
        Assert.Contains("\"authorId\":null", ownJson);
        Assert.Contains("\"displayName\":null", ownJson);
        Assert.DoesNotContain("Post Author", ownJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(poster.Value.ToString(), ownJson, StringComparison.OrdinalIgnoreCase);

        CommunityCommentResponse other = Assert.Single(comments, item => item.AuthorId is not null);
        Assert.False(other.IsAnonymous);
        Assert.Equal(visitor.Value, other.AuthorId);
        Assert.Equal("Visitor", other.DisplayName);
    }

    [Fact]
    public async Task Reply_written_by_the_author_of_an_anonymous_post_carries_no_identity()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var poster = UserId.New();
        var visitor = UserId.New();
        await harness.AddIdentityUserAsync(poster.Value, "المؤلف", "Post Author");
        await harness.AddIdentityUserAsync(visitor.Value, "زائر", "Visitor");

        Guid postId = await harness.AddPublishedPostAsync(poster.Value, isAnonymous: true);
        Guid commentId = await harness.AddCommentAsync(postId, visitor.Value);
        await harness.AddReplyAsync(commentId, poster.Value);
        await harness.AddReplyAsync(commentId, visitor.Value);

        CommentsController controller = harness.CreateCommentsController(UserId.New());
        IReadOnlyList<CommunityReplyResponse> replies = harness.ReadReplies(
            await controller.GetCommentReplies(commentId, cancellationToken: default));

        CommunityReplyResponse own = Assert.Single(replies, item => item.AuthorId is null);
        Assert.True(own.IsAnonymous);
        Assert.Null(own.DisplayName);
        Assert.Null(own.AvatarUrl);
        string ownJson = harness.Serialize(own);
        Assert.Contains("\"authorId\":null", ownJson);
        Assert.DoesNotContain("Post Author", ownJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(poster.Value.ToString(), ownJson, StringComparison.OrdinalIgnoreCase);

        CommunityReplyResponse other = Assert.Single(replies, item => item.AuthorId is not null);
        Assert.False(other.IsAnonymous);
        Assert.Equal(visitor.Value, other.AuthorId);
        Assert.Equal("Visitor", other.DisplayName);
    }

    [Fact]
    public async Task Comment_list_keeps_the_display_name_null_when_the_identity_row_is_missing()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var author = UserId.New();
        Guid postId = await harness.AddPublishedPostAsync(author.Value, isAnonymous: false);
        await harness.AddCommentAsync(postId, author.Value);

        CommentsController controller = harness.CreateCommentsController(UserId.New());
        IReadOnlyList<CommunityCommentResponse> comments = harness.ReadComments(
            await controller.GetPostComments(postId, cancellationToken: default));

        CommunityCommentResponse comment = Assert.Single(comments);
        Assert.False(comment.IsAnonymous);
        Assert.Null(comment.DisplayName);
        Assert.Null(comment.AvatarUrl);
    }

    [Fact]
    public async Task Comment_and_reply_lists_resolve_the_author_names_with_one_batched_lookup_per_page()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();
        var poster = UserId.New();
        var visitor = UserId.New();
        await harness.AddIdentityUserAsync(poster.Value, "المؤلف", "Post Author");
        await harness.AddIdentityUserAsync(visitor.Value, "زائر", "Visitor");

        Guid postId = await harness.AddPublishedPostAsync(poster.Value, isAnonymous: true);
        Guid commentId = await harness.AddCommentAsync(postId, visitor.Value);
        await harness.AddCommentAsync(postId, visitor.Value);
        await harness.AddReplyAsync(commentId, visitor.Value);

        CommentsController controller = harness.CreateCommentsController(UserId.New());
        harness.ReadComments(await controller.GetPostComments(postId, cancellationToken: default));
        Assert.Equal(1, harness.AuthorNameLookupCount);
        Assert.Equal(new[] { visitor.Value }, harness.LastRequestedAuthorIds.ToArray());

        harness.ReadReplies(await controller.GetCommentReplies(commentId, cancellationToken: default));
        Assert.Equal(2, harness.AuthorNameLookupCount);
        Assert.Equal(new[] { visitor.Value }, harness.LastRequestedAuthorIds.ToArray());
    }
}
