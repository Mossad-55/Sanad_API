using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Comments;
using Sanad.Modules.Community.Application.CheckIns;
using Sanad.Modules.Community.Application.HelpRequests;
using Sanad.Modules.Community.Application.Posts;
using Sanad.Modules.Community.Application.Ratings;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Domain.Comments;

namespace Sanad.API.Controllers;

[Authorize]
[Route("api/v1/community")]
public sealed class CommunityPostsController : ApiControllerBase
{
    private readonly ISender _sender;

    public CommunityPostsController(ISender sender)
    {
        _sender = sender;
    }

    #region Posts

    [HttpGet("posts")]
    [ProducesResponseType(
        typeof(IReadOnlyList<CommunityPostResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPosts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var postsSort = CommunityPostsSort.Newest;
        if (!string.IsNullOrWhiteSpace(sort) &&
            !Enum.TryParse(sort, ignoreCase: true, out postsSort))
        {
            return BadRequestWithCode("Community.InvalidSort");
        }

        if (fromDate is DateTime from && toDate is DateTime to && from > to)
        {
            return BadRequestWithCode("Community.InvalidDateRange");
        }

        var query = new GetPostsQuery(
            PostStatus.Published, page, pageSize, search, categoryId,
            fromDate, toDate, postsSort, userId.Value);
        var result = await _sender.Send(query, cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);

        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles =
            await LoadAuthorProfilesAsync(
                result.Value
                    .Where(item => !item.Post.IsAnonymous)
                    .Select(item => item.Post.AuthorId.Value),
                cancellationToken);

        return Ok(result.Value.Select(item => ToResponse(item, userId.Value, profiles)));
    }

    [HttpGet("favorites")]
    [ProducesResponseType(
        typeof(IReadOnlyList<CommunityPostResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFavorites(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var query = new GetFavoritePostsQuery(userId.Value, page, pageSize);
        var result = await _sender.Send(query, cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);

        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles =
            await LoadAuthorProfilesAsync(
                result.Value
                    .Where(item => !item.Post.IsAnonymous)
                    .Select(item => item.Post.AuthorId.Value),
                cancellationToken);

        return Ok(result.Value.Select(item => ToResponse(item, userId.Value, profiles)));
    }

    [HttpGet("recommendations")]
    [ProducesResponseType(
        typeof(IReadOnlyList<CommunityPostResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecommendations(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var query = new GetCommunityRecommendationsQuery(userId.Value, page, pageSize);
        var result = await _sender.Send(query, cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);

        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles =
            await LoadAuthorProfilesAsync(
                result.Value
                    .Where(post => !post.IsAnonymous)
                    .Select(post => post.AuthorId.Value),
                cancellationToken);

        return Ok(result.Value.Select(post => ToResponse(post, userId.Value, profiles)));
    }

    [HttpGet("posts/{postId:guid}")]
    [ProducesResponseType(
        typeof(CommunityPostResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPost(
        Guid postId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var query = new GetPostQuery(postId, userId.Value);
        var result = await _sender.Send(query, cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);
        if (result.Value?.Post is null) return NotFound();

        Guid[] authorIds = result.Value.Post.IsAnonymous
            ? Array.Empty<Guid>()
            : new[] { result.Value.Post.AuthorId.Value };

        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles =
            await LoadAuthorProfilesAsync(authorIds, cancellationToken);

        return Ok(ToResponse(result.Value, userId.Value, profiles));
    }

    [HttpPost("posts")]
    [ProducesResponseType(
        typeof(CommunityPostResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> CreatePost(
        [FromBody] CreatePostCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        command = command with { AuthorId = userId.Value };
        var result = await _sender.Send(command, cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);

        Guid[] authorIds = result.Value.IsAnonymous
            ? Array.Empty<Guid>()
            : new[] { result.Value.AuthorId.Value };

        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles =
            await LoadAuthorProfilesAsync(authorIds, cancellationToken);

        return CreatedAtAction(
            nameof(GetPost),
            new { postId = result.Value.Id.Value },
            ToResponse(result.Value, userId.Value, profiles));
    }

    [HttpPost("posts/{postId:guid}/like")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> LikePost(
        Guid postId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var command = new LikePostCommand { PostId = postId, UserId = userId.Value };

        var result = await _sender.Send(command, cancellationToken);
        return result.IsSuccess ? Ok() : ToActionResult(result);
    }

    /// <summary>
    /// FUR post management (Owner addition, locked 2026-10-23): the
    /// authenticated author edits THEIR OWN post. The acting author id comes
    /// from the authentication claim only — a request body can never supply
    /// or change it, so app ownership cannot be bypassed by a forged id —
    /// and the stored author identity stays immutable. Editing preserves
    /// moderation: published content re-enters the pending-review queue
    /// instead of bypassing the approval requirement.
    /// <para>
    /// The handler reuses the shared error codes, so the wire statuses stay
    /// in the one shared mapper: unknown/foreign/deleted post → 404
    /// (privacy not-found), a rejected/archived post or a stale concurrent
    /// write → 409, an unauthorized image reference → 400. Success is 200
    /// with the freshly persisted post. The image reference, when present,
    /// must be an authorized Community upload (own upload or already
    /// published); external URLs and another user's private asset are
    /// rejected.
    /// </para>
    /// </summary>
    [HttpPut("posts/{postId:guid}")]
    [ProducesResponseType(
        typeof(CommunityPostResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdatePost(
        Guid postId,
        [FromBody] UpdatePostCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        // The route id and the claim own these two fields; the body can never
        // override them.
        command = command with { PostId = postId, ActingAuthorId = userId.Value };

        var result = await _sender.Send(command, cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);

        Guid[] authorIds = result.Value.IsAnonymous
            ? Array.Empty<Guid>()
            : new[] { result.Value.AuthorId.Value };

        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles =
            await LoadAuthorProfilesAsync(authorIds, cancellationToken);

        return Ok(ToResponse(result.Value, userId.Value, profiles));
    }

    /// <summary>
    /// FUR post management: the authenticated author deletes THEIR OWN post.
    /// The acting author id is the claim value only. Deletion is a retained
    /// transition (status Deleted with DeletedBy/DeletedOnUtc audit) — the
    /// children and the referenced media stay stored, no cascading removal.
    /// The post disappears from every public read at once; a repeat delete
    /// and a foreign author's delete answer the same 404.
    /// </summary>
    [HttpDelete("posts/{postId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeletePost(
        Guid postId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new DeletePostCommand(postId, userId.Value),
            cancellationToken);

        return result.IsSuccess ? NoContent() : ToActionResult(result);
    }

    [HttpDelete("posts/{postId:guid}/like")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnlikePost(
        Guid postId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var command = new UnlikePostCommand(postId, userId.Value);
        var result = await _sender.Send(command, cancellationToken);

        if (!result.IsSuccess) return ToActionResult(result);
        if (!result.Value)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpPost("posts/{postId:guid}/favorite")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> FavoritePost(
        Guid postId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var command = new ToggleFavoritePostCommand(postId, userId.Value);
        var result = await _sender.Send(command, cancellationToken);
        return result.IsSuccess ? Ok() : ToActionResult(result);
    }

    /// <summary>
    /// FUR-07 report intake. Published posts only; an author cannot report
    /// their own post; anonymous callers are rejected by the controller-level
    /// authorization. A repeat request returns the same retained report.
    /// </summary>
    [HttpPost("posts/{postId:guid}/reports")]
    [ProducesResponseType(
        typeof(SubmitCommunityPostReportResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReportPost(
        Guid postId,
        [FromBody] SubmitCommunityPostReportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new SubmitCommunityPostReportCommand(postId, userId, request.ReasonCode, request.Note),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : ToReportError(result.Error);
    }

    #endregion

    #region Check-ins

    [HttpPost("posts/{postId:guid}/check-in")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddCheckIn(
        Guid postId,
        [FromBody] AddCheckInCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        command = command with { PostId = postId, UserId = userId.Value };
        var result = await _sender.Send(command, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToActionResult(result);
    }

    #endregion

    #region Ratings

    [HttpPost("posts/{postId:guid}/rating")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddRating(
        Guid postId,
        [FromBody] AddRatingCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        command = command with { PostId = postId, UserId = userId.Value };
        var result = await _sender.Send(command, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToActionResult(result);
    }

    [HttpGet("posts/{postId:guid}/rating/average")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPostRatingAverage(
        Guid postId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out _))
        {
            return Unauthorized();
        }

        var query = new GetPostRatingAverageQuery(postId);
        var result = await _sender.Send(query, cancellationToken);
        return ToActionResult(result);
    }

    #endregion

    #region Help Request Catalog

    [HttpGet("help-request-catalog")]
    [ProducesResponseType(
        typeof(IReadOnlyList<HelpRequestCatalogItem>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHelpRequestCatalog(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out _))
        {
            return Unauthorized();
        }

        var query = new GetElderlyHelpRequestCatalogQuery();
        var result = await _sender.Send(query, cancellationToken);
        return ToActionResult(result);
    }

    #endregion

    /// <summary>
    /// Forces the report-intake state conflicts to 409 (own post, or a target
    /// that is not published) instead of the shared mapper's 400 default; the
    /// unknown-post 404 already comes from the mapper.
    /// </summary>
    private IActionResult ToReportError(Error error)
    {
        if (error.Code == CommunityModerationErrors.CannotReportOwnPost.Code ||
            error.Code == CommunityModerationErrors.ReportNotAllowed.Code)
        {
            ProblemDetails problemDetails =
                ResultProblemDetailsMapper.Create(error, HttpContext);
            problemDetails.Type = "https://httpstatuses.com/409";
            problemDetails.Title = "Conflict";
            problemDetails.Status = StatusCodes.Status409Conflict;
            problemDetails.Detail = "The request conflicts with the current state.";
            return StatusCode(StatusCodes.Status409Conflict, problemDetails);
        }

        return ToActionResult(Result.Failure(error));
    }

    private static CommunityPostResponse ToResponse(PostFeedItem item, Guid viewerUserId,
        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles) =>
        ToResponse(item.Post, item.IsLiked, item.IsFavorited, item.CurrentUserRating,
            viewerUserId, profiles);

    private static CommunityPostResponse ToResponse(Post post, Guid viewerUserId,
        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles) =>
        ToResponse(post, false, false, null, viewerUserId, profiles);

    private static CommunityPostResponse ToResponse(
        Post post,
        bool isLiked,
        bool isFavorited,
        int? currentUserRating,
        Guid viewerUserId,
        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles)
    {
        // An anonymous post carries no author identity at all: no display name,
        // no avatar and no author id (this response never carried one).
        bool anonymous = post.IsAnonymous;
        profiles.TryGetValue(post.AuthorId.Value, out CommunityAuthorProfile? author);

        // The viewer-specific capability snapshot describes this read only: it
        // derives from the authenticated viewer and the stored author/status,
        // and never widens what the mutation endpoints already enforce. An
        // anonymous author still sees their own true flags even though the
        // author identity above stays hidden exactly as before.
        (bool isOwn, bool canEdit, bool canDelete) =
            CommunityPostResponse.ResolveCapabilities(post, viewerUserId);

        return new CommunityPostResponse(
            post.Id.Value, post.TitleArabic, post.TitleEnglish, post.ContentArabic,
            post.ContentEnglish, post.ImageUrl, post.IsAnonymous, post.Status.ToString(),
            post.LikesCount, post.CommentsCount, post.FavoritesCount, post.CreatedOnUtc,
            post.CategoryId?.Value, post.Category?.ArabicName, post.Category?.EnglishName,
            isLiked, isFavorited, currentUserRating,
            anonymous ? null : author?.DisplayName,
            anonymous ? null : author?.AvatarUrl,
            isOwn, canEdit, canDelete);
    }

    /// <summary>
    /// One batched identity lookup for the authors of the page. The ids of
    /// anonymous posts are never sent: an anonymous author is not looked up and
    /// is not projected.
    /// </summary>
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
}

/// <summary>
/// One Community post as the mobile/consumer surface returns it. The three
/// trailing capability flags are a **viewer-specific read snapshot**:
/// <c>IsOwn</c> is true only when the server-authenticated caller is the stored
/// author, <c>CanEdit</c> additionally requires a <c>PendingReview</c> or
/// <c>Published</c> post and <c>CanDelete</c> additionally requires a post that
/// is not <c>Deleted</c>. They grant nothing: the PUT/DELETE routes keep
/// enforcing ownership, status and concurrent-state checks server-side, and
/// moderation powers stay on the separate Admin routes.
/// </summary>
public sealed record CommunityPostResponse(Guid Id, string TitleArabic, string TitleEnglish,
    string ContentArabic, string ContentEnglish, string? ImageUrl, bool IsAnonymous,
    string Status, int LikesCount, int CommentsCount, int FavoritesCount, DateTime CreatedOnUtc,
    Guid? CategoryId = null, string? CategoryArabicName = null, string? CategoryEnglishName = null,
    bool IsLiked = false, bool IsFavorited = false, int? CurrentUserRating = null,
    string? DisplayName = null, string? AvatarUrl = null,
    bool IsOwn = false, bool CanEdit = false, bool CanDelete = false)
{
    /// <summary>
    /// The locked viewer-capability rule, in one place: ownership is the
    /// stored author compared against the server-authenticated viewer id;
    /// edit is allowed on the author's <c>PendingReview</c>/<c>Published</c>
    /// post; delete on the author's post that is not <c>Deleted</c>. A foreign
    /// viewer — and an empty/unauthenticated viewer id — gets <c>false</c> for
    /// all three. Anonymity never changes the flags: an anonymous author sees
    /// their own capabilities while the author identity stays hidden.
    /// </summary>
    public static (bool IsOwn, bool CanEdit, bool CanDelete) ResolveCapabilities(
        Post post, Guid viewerUserId)
    {
        bool isOwn = viewerUserId != Guid.Empty &&
            post.AuthorId == new UserId(viewerUserId);
        bool canEdit = isOwn &&
            post.Status is PostStatus.PendingReview or PostStatus.Published;
        bool canDelete = isOwn && post.Status != PostStatus.Deleted;

        return (isOwn, canEdit, canDelete);
    }
}

public sealed record SubmitCommunityPostReportRequest(string? ReasonCode, string? Note);
