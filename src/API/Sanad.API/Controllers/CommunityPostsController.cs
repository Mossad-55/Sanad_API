using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.BuildingBlocks.Application.Abstractions;
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
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out _))
        {
            return Unauthorized();
        }

        var query = new GetPostsQuery(PostStatus.Published, page, pageSize);
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
            ? Ok(result.Value.Select(ToResponse))
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
        if (!TryGetAuthenticatedUserId(out _))
        {
            return Unauthorized();
        }

        var query = new GetPostQuery(postId);
        var result = await _sender.Send(query, cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);
        return result.Value is null ? NotFound() : Ok(ToResponse(result.Value));
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

    private static CommunityPostResponse ToResponse(Post post) => new(
        post.Id.Value, post.TitleArabic, post.TitleEnglish, post.ContentArabic,
        post.ContentEnglish, post.ImageUrl, post.IsAnonymous, post.Status.ToString(),
        post.LikesCount, post.CommentsCount, post.FavoritesCount, post.CreatedOnUtc);
}

public sealed record CommunityPostResponse(Guid Id, string TitleArabic, string TitleEnglish,
    string ContentArabic, string ContentEnglish, string? ImageUrl, bool IsAnonymous,
    string Status, int LikesCount, int CommentsCount, int FavoritesCount, DateTime CreatedOnUtc);
