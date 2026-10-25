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
        CommunityCommentViewerState state = await LoadCommentViewerStateAsync(
            new[] { result.Value }, Array.Empty<Reply>(), userId.Value, cancellationToken);

        return CreatedAtAction(
            nameof(GetPostComments),
            new { postId },
            ToResponse(result.Value, thread, profiles, state, userId.Value));
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
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var query = new GetCommentsQuery(postId, page, pageSize);
        var result = await _sender.Send(query, cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);

        CommunityThreadPost? thread = await LoadThreadPostAsync(postId, null, cancellationToken);
        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles =
            await LoadAuthorProfilesAsync(result.Value, thread, cancellationToken);
        CommunityCommentViewerState state = await LoadCommentViewerStateAsync(
            result.Value, Array.Empty<Reply>(), userId.Value, cancellationToken);

        return Ok(result.Value.Select(
            comment => ToResponse(comment, thread, profiles, state, userId.Value)));
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
        CommunityCommentViewerState state = await LoadCommentViewerStateAsync(
            new[] { result.Value }, Array.Empty<Reply>(), userId.Value, cancellationToken);

        return Ok(ToResponse(result.Value, thread, profiles, state, userId.Value));
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
        CommunityCommentViewerState state = await LoadCommentViewerStateAsync(
            Array.Empty<Comment>(), new[] { result.Value }, userId.Value, cancellationToken);

        return CreatedAtAction(
            nameof(GetCommentReplies),
            new { commentId },
            ToResponse(result.Value, thread, profiles, state, userId.Value));
    }

    [HttpGet("comments/{commentId:guid}/replies")]
    [ProducesResponseType(typeof(IReadOnlyList<CommunityReplyResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCommentReplies(
        Guid commentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new GetCommentRepliesQuery(commentId, page, pageSize), cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);

        CommunityThreadPost? thread = await LoadThreadPostAsync(null, commentId, cancellationToken);
        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles =
            await LoadAuthorProfilesAsync(result.Value, thread, cancellationToken);
        CommunityCommentViewerState state = await LoadCommentViewerStateAsync(
            Array.Empty<Comment>(), result.Value, userId.Value, cancellationToken);

        return Ok(result.Value.Select(
            reply => ToResponse(reply, thread, profiles, state, userId.Value)));
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
    /// The batched persisted viewer state of this page: the caller's likes and
    /// the comments that already have replies. One bounded query set per page,
    /// never one per row. A failed lookup degrades to empty flags (all false /
    /// not liked) rather than inventing a capability.
    /// </summary>
    private async Task<CommunityCommentViewerState> LoadCommentViewerStateAsync(
        IReadOnlyCollection<Comment> comments,
        IReadOnlyCollection<Reply> replies,
        Guid viewerUserId,
        CancellationToken cancellationToken)
    {
        Result<CommunityCommentViewerState> result = await _sender.Send(
            new GetCommunityCommentViewerStateQuery(
                comments.Select(comment => comment.Id.Value).ToArray(),
                replies.Select(reply => reply.Id.Value).ToArray(),
                viewerUserId),
            cancellationToken);

        return result.IsSuccess ? result.Value : CommunityCommentViewerState.Empty;
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

    /// <summary>
    /// The comment capability snapshot mirrors the REAL mutation handlers:
    /// <list type="bullet">
    /// <item><c>isOwn</c> — the caller is the stored comment author. On an
    /// anonymous comment only that author sees <c>true</c>; every other caller
    /// sees <c>false</c>, and no identity is added.</item>
    /// <item><c>canEdit</c> — exactly the update rule (ownership; the content
    /// validation depends on the submitted body, not on this read).</item>
    /// <item><c>canDelete</c> — exactly the delete rule (ownership AND no
    /// replies, because the delete handler answers <c>CommentHasReplies</c>).</item>
    /// <item><c>isLiked</c> — the caller's persisted comment like.</item>
    /// </list>
    /// No moderator/admin comment power exists in the API, so none is encoded.
    /// </summary>
    private static CommunityCommentResponse ToResponse(
        Comment comment,
        CommunityThreadPost? thread,
        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles,
        CommunityCommentViewerState state,
        Guid viewerUserId)
    {
        bool isAnonymous = IsAnonymous(comment.AuthorId.Value, thread);
        profiles.TryGetValue(comment.AuthorId.Value, out CommunityAuthorProfile? author);
        var viewer = new UserId(viewerUserId);
        bool isOwn = viewerUserId != Guid.Empty && comment.AuthorId == viewer;

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
            isAnonymous ? null : author?.AvatarUrl,
            isOwn,
            isOwn,
            isOwn && !state.CommentIdsWithReplies.Contains(comment.Id.Value),
            state.LikedCommentIds.Contains(comment.Id.Value));
    }

    /// <summary>
    /// Replies carry the same four flags for a consistent shape, but the API
    /// exposes no reply edit/delete route, so <c>canEdit</c> and
    /// <c>canDelete</c> are always <c>false</c> — never a capability the
    /// caller cannot exercise. <c>isLiked</c> is the caller's persisted like on
    /// the reply id.
    /// </summary>
    private static CommunityReplyResponse ToResponse(
        Reply reply,
        CommunityThreadPost? thread,
        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles,
        CommunityCommentViewerState state,
        Guid viewerUserId)
    {
        bool isAnonymous = IsAnonymous(reply.AuthorId.Value, thread);
        profiles.TryGetValue(reply.AuthorId.Value, out CommunityAuthorProfile? author);
        var viewer = new UserId(viewerUserId);
        bool isOwn = viewerUserId != Guid.Empty && reply.AuthorId == viewer;

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
            isAnonymous ? null : author?.AvatarUrl,
            isOwn,
            false,
            false,
            state.LikedReplyIds.Contains(reply.Id.Value));
    }
}

public sealed record CommunityCommentResponse(Guid Id, Guid PostId, Guid? AuthorId, string ContentArabic,
    string ContentEnglish, bool IsEdited, DateTime CreatedOnUtc, DateTime? UpdatedOnUtc, int LikesCount,
    bool IsAnonymous = false, string? DisplayName = null, string? AvatarUrl = null,
    bool IsOwn = false, bool CanEdit = false, bool CanDelete = false, bool IsLiked = false);
public sealed record CommunityReplyResponse(Guid Id, Guid CommentId, Guid? AuthorId, string ContentArabic,
    string ContentEnglish, bool IsEdited, DateTime CreatedOnUtc, DateTime? UpdatedOnUtc, int LikesCount,
    bool IsAnonymous = false, string? DisplayName = null, string? AvatarUrl = null,
    bool IsOwn = false, bool CanEdit = false, bool CanDelete = false, bool IsLiked = false);
