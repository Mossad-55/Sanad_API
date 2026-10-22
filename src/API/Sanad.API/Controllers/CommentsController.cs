using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Community.Application.Comments;
using Sanad.Modules.Community.Application.Posts;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.API.Controllers;

[Authorize]
[Route("api/v1/community")]
public sealed class CommentsController : ApiControllerBase
{
    private readonly ISender _sender;

    public CommentsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("posts/{postId:guid}/comments")]
    [ProducesResponseType(
        typeof(CommunityCommentResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateComment(
        Guid postId,
        [FromBody] CreateCommentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        command = command with { AuthorId = userId.Value, PostId = postId };
        var result = await _sender.Send(command, cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);

        CommunityThreadPost? thread = await LoadThreadPostAsync(postId, null, cancellationToken);
        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles =
            await LoadAuthorProfilesAsync(new[] { result.Value }, thread, cancellationToken);

        return CreatedAtAction(
            nameof(GetPostComments),
            new { postId },
            ToResponse(result.Value, thread, profiles));
    }

    [HttpGet("posts/{postId:guid}/comments")]
    [ProducesResponseType(
        typeof(IReadOnlyList<CommunityCommentResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPostComments(
        Guid postId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out _))
        {
            return Unauthorized();
        }

        var query = new GetCommentsQuery(postId, page, pageSize);
        var result = await _sender.Send(query, cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);

        CommunityThreadPost? thread = await LoadThreadPostAsync(postId, null, cancellationToken);
        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles =
            await LoadAuthorProfilesAsync(result.Value, thread, cancellationToken);

        return Ok(result.Value.Select(comment => ToResponse(comment, thread, profiles)));
    }

    [HttpPut("comments/{commentId:guid}")]
    [ProducesResponseType(
        typeof(CommunityCommentResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateComment(
        Guid commentId,
        [FromBody] UpdateCommentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        command = command with { CommentId = commentId, AuthorId = userId.Value };

        var result = await _sender.Send(command, cancellationToken);

        if (!result.IsSuccess) return ToActionResult(result);
        if (result.Value is null) return NotFound();

        CommunityThreadPost? thread = await LoadThreadPostAsync(
            result.Value.PostId.Value, null, cancellationToken);
        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles =
            await LoadAuthorProfilesAsync(new[] { result.Value }, thread, cancellationToken);

        return Ok(ToResponse(result.Value, thread, profiles));
    }

    [HttpDelete("comments/{commentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteComment(
        Guid commentId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var command = new DeleteCommentCommand(commentId, userId.Value);
        var result = await _sender.Send(command, cancellationToken);

        if (!result.IsSuccess) return ToActionResult(result);
        if (!result.Value.Success)
        {
            return result.Value.Error is "NotFound" or "Forbidden"
                ? NotFound()
                : result.Value.Error == "CommentHasReplies"
                    ? Conflict()
                    : BadRequest();
        }

        return NoContent(); // 204 No Content - successfully deleted
    }

    [HttpPost("comments/{commentId:guid}/like")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> LikeComment(
        Guid commentId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new LikeCommunityCommentCommand(commentId, userId.Value), cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);
        return result.Value ? Ok() : NotFound();
    }

    [HttpPost("comments/{commentId:guid}/replies")]
    [ProducesResponseType(typeof(CommunityReplyResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateReply(
        Guid commentId,
        [FromBody] CreateReplyCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        command = command with { AuthorId = userId.Value, CommentId = commentId };
        var result = await _sender.Send(command, cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);

        CommunityThreadPost? thread = await LoadThreadPostAsync(null, commentId, cancellationToken);
        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles =
            await LoadAuthorProfilesAsync(new[] { result.Value }, thread, cancellationToken);

        return CreatedAtAction(
            nameof(GetCommentReplies),
            new { commentId },
            ToResponse(result.Value, thread, profiles));
    }

    [HttpGet("comments/{commentId:guid}/replies")]
    [ProducesResponseType(typeof(IReadOnlyList<CommunityReplyResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCommentReplies(
        Guid commentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out _))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new GetCommentRepliesQuery(commentId, page, pageSize), cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);

        CommunityThreadPost? thread = await LoadThreadPostAsync(null, commentId, cancellationToken);
        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles =
            await LoadAuthorProfilesAsync(result.Value, thread, cancellationToken);

        return Ok(result.Value.Select(reply => ToResponse(reply, thread, profiles)));
    }

    /// <summary>
    /// The post that owns the comment thread. It carries the post's stored
    /// anonymity flag and author, which decide whether a comment or a reply is
    /// presented as anonymous.
    /// </summary>
    private async Task<CommunityThreadPost?> LoadThreadPostAsync(
        Guid? postId,
        Guid? commentId,
        CancellationToken cancellationToken)
    {
        if (postId is Guid knownPostId)
        {
            var byPost = await _sender.Send(
                new GetCommunityThreadPostByPostIdQuery(knownPostId),
                cancellationToken);

            return byPost.IsSuccess ? byPost.Value : null;
        }

        if (commentId is Guid knownCommentId)
        {
            var byComment = await _sender.Send(
                new GetCommunityThreadPostByCommentIdQuery(knownCommentId),
                cancellationToken);

            return byComment.IsSuccess ? byComment.Value : null;
        }

        return null;
    }

    /// <summary>
    /// One batched identity lookup for the authors of the page. The ids of
    /// anonymous comments and replies are never sent: an anonymous author is
    /// not looked up and is not projected.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, CommunityAuthorProfile>> LoadAuthorProfilesAsync(
        IEnumerable<Comment> comments,
        CommunityThreadPost? thread,
        CancellationToken cancellationToken) =>
        await LoadAuthorProfilesAsync(
            comments
                .Where(comment => !IsAnonymous(comment.AuthorId.Value, thread))
                .Select(comment => comment.AuthorId.Value),
            cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, CommunityAuthorProfile>> LoadAuthorProfilesAsync(
        IEnumerable<Reply> replies,
        CommunityThreadPost? thread,
        CancellationToken cancellationToken) =>
        await LoadAuthorProfilesAsync(
            replies
                .Where(reply => !IsAnonymous(reply.AuthorId.Value, thread))
                .Select(reply => reply.AuthorId.Value),
            cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, CommunityAuthorProfile>> LoadAuthorProfilesAsync(
        IEnumerable<Guid> authorIds,
        CancellationToken cancellationToken)
    {
        Guid[] ids = authorIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, CommunityAuthorProfile>();
        }

        Result<IReadOnlyDictionary<Guid, CommunityAuthorProfile>> result =
            await _sender.Send(
                new GetCommunityAuthorProfilesQuery(ids),
                cancellationToken);

        return result.IsSuccess
            ? result.Value
            : new Dictionary<Guid, CommunityAuthorProfile>();
    }

    /// <summary>
    /// A comment or a reply is presented as anonymous when it is written by the
    /// author of an anonymous post inside that post's own thread: the post
    /// author must never be identified through their own words.
    /// </summary>
    private static bool IsAnonymous(Guid authorId, CommunityThreadPost? thread) =>
        thread is not null &&
        thread.IsAnonymous &&
        thread.AuthorId == authorId;

    private static CommunityCommentResponse ToResponse(
        Comment comment,
        CommunityThreadPost? thread,
        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles)
    {
        bool isAnonymous = IsAnonymous(comment.AuthorId.Value, thread);
        profiles.TryGetValue(comment.AuthorId.Value, out CommunityAuthorProfile? author);

        return new CommunityCommentResponse(
            comment.Id.Value,
            comment.PostId.Value,
            isAnonymous ? null : comment.AuthorId.Value,
            comment.ContentArabic,
            comment.ContentEnglish,
            comment.IsEdited,
            comment.CreatedOnUtc,
            comment.UpdatedOnUtc,
            comment.LikesCount,
            isAnonymous,
            isAnonymous ? null : author?.DisplayName,
            isAnonymous ? null : author?.AvatarUrl);
    }

    private static CommunityReplyResponse ToResponse(
        Reply reply,
        CommunityThreadPost? thread,
        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles)
    {
        bool isAnonymous = IsAnonymous(reply.AuthorId.Value, thread);
        profiles.TryGetValue(reply.AuthorId.Value, out CommunityAuthorProfile? author);

        return new CommunityReplyResponse(
            reply.Id.Value,
            reply.CommentId.Value,
            isAnonymous ? null : reply.AuthorId.Value,
            reply.ContentArabic,
            reply.ContentEnglish,
            reply.IsEdited,
            reply.CreatedOnUtc,
            reply.UpdatedOnUtc,
            reply.LikesCount,
            isAnonymous,
            isAnonymous ? null : author?.DisplayName,
            isAnonymous ? null : author?.AvatarUrl);
    }
}

public sealed record CommunityCommentResponse(Guid Id, Guid PostId, Guid? AuthorId, string ContentArabic,
    string ContentEnglish, bool IsEdited, DateTime CreatedOnUtc, DateTime? UpdatedOnUtc, int LikesCount,
    bool IsAnonymous = false, string? DisplayName = null, string? AvatarUrl = null);
public sealed record CommunityReplyResponse(Guid Id, Guid CommentId, Guid? AuthorId, string ContentArabic,
    string ContentEnglish, bool IsEdited, DateTime CreatedOnUtc, DateTime? UpdatedOnUtc, int LikesCount,
    bool IsAnonymous = false, string? DisplayName = null, string? AvatarUrl = null);
