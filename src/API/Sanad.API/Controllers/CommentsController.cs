using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.Modules.Community.Application.Comments;
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
        return CreatedAtAction(nameof(GetPostComments), new { postId }, ToResponse(result.Value));
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
        return result.IsSuccess
            ? Ok(result.Value.Select(ToResponse))
            : ToActionResult(result);
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
        return result.Value is null ? NotFound() : Ok(ToResponse(result.Value));
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
        return CreatedAtAction(nameof(GetCommentReplies), new { commentId }, ToResponse(result.Value));
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
        return result.IsSuccess
            ? Ok(result.Value.Select(ToResponse))
            : ToActionResult(result);
    }
    private static CommunityCommentResponse ToResponse(Comment comment) => new(
        comment.Id.Value, comment.PostId.Value, comment.AuthorId.Value, comment.ContentArabic,
        comment.ContentEnglish, comment.IsEdited, comment.CreatedOnUtc, comment.UpdatedOnUtc,
        comment.LikesCount);

    private static CommunityReplyResponse ToResponse(Reply reply) => new(
        reply.Id.Value, reply.CommentId.Value, reply.AuthorId.Value, reply.ContentArabic,
        reply.ContentEnglish, reply.IsEdited, reply.CreatedOnUtc, reply.UpdatedOnUtc, reply.LikesCount);
}

public sealed record CommunityCommentResponse(Guid Id, Guid PostId, Guid AuthorId, string ContentArabic,
    string ContentEnglish, bool IsEdited, DateTime CreatedOnUtc, DateTime? UpdatedOnUtc, int LikesCount);
public sealed record CommunityReplyResponse(Guid Id, Guid CommentId, Guid AuthorId, string ContentArabic,
    string ContentEnglish, bool IsEdited, DateTime CreatedOnUtc, DateTime? UpdatedOnUtc, int LikesCount);
