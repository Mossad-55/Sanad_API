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
        return result.IsSuccess
            ? Ok(result.Value.Select(ToResponse))
            : ToActionResult(result);
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
        return result.IsSuccess
            ? Ok(result.Value.Select(ToResponse))
            : ToActionResult(result);
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
        return result.IsSuccess
            ? Ok(result.Value.Select(p => ToResponse(p)))
            : ToActionResult(result);
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
        return result.Value?.Post is null ? NotFound() : Ok(ToResponse(result.Value));
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
        return CreatedAtAction(nameof(GetPost), new { postId = result.Value.Id.Value }, ToResponse(result.Value));
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

    private static CommunityPostResponse ToResponse(PostFeedItem item) =>
        ToResponse(item.Post, item.IsLiked, item.IsFavorited, item.CurrentUserRating);

    private static CommunityPostResponse ToResponse(
        Post post,
        bool isLiked = false,
        bool isFavorited = false,
        int? currentUserRating = null) => new(
        post.Id.Value, post.TitleArabic, post.TitleEnglish, post.ContentArabic,
        post.ContentEnglish, post.ImageUrl, post.IsAnonymous, post.Status.ToString(),
        post.LikesCount, post.CommentsCount, post.FavoritesCount, post.CreatedOnUtc,
        post.CategoryId?.Value, post.Category?.ArabicName, post.Category?.EnglishName,
        isLiked, isFavorited, currentUserRating);
}

public sealed record CommunityPostResponse(Guid Id, string TitleArabic, string TitleEnglish,
    string ContentArabic, string ContentEnglish, string? ImageUrl, bool IsAnonymous,
    string Status, int LikesCount, int CommentsCount, int FavoritesCount, DateTime CreatedOnUtc,
    Guid? CategoryId = null, string? CategoryArabicName = null, string? CategoryEnglishName = null,
    bool IsLiked = false, bool IsFavorited = false, int? CurrentUserRating = null);

public sealed record SubmitCommunityPostReportRequest(string? ReasonCode, string? Note);
