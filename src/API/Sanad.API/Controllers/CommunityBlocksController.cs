using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Blocks;

namespace Sanad.API.Controllers;

/// <summary>
/// The body of a Community block request: the author to block, by id only.
/// </summary>
public sealed record BlockCommunityAuthorRequest(Guid AuthorId);

/// <summary>
/// The viewer's own Community author blocks: one-way, hide-posts-only. The
/// relation is Community-owned — no Chat block is read or written here.
/// </summary>
[Authorize]
[Route("api/v1/community/blocks")]
public sealed class CommunityBlocksController : ApiControllerBase
{
    private readonly ISender _sender;

    public CommunityBlocksController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Block an author (204, idempotent). A self-block is a 400
    /// <see cref="CommunityBlockErrors.Invalid"/>: the caller is entitled to
    /// know their own request was invalid. An unknown target author answers
    /// the generic 404 body; a known target answers 204 and an unknown one
    /// 404, so the route still separates a registered account id from an
    /// unregistered one — the accepted Chat block design. Blocking only hides
    /// that author's **posts** from the caller: comments, replies and every
    /// other interaction are unchanged.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BlockAuthor(
        [FromBody] BlockCommunityAuthorRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new BlockCommunityAuthorCommand(userId, new UserId(request.AuthorId)),
            cancellationToken);

        return ToCommunityBlockActionResult(result);
    }

    /// <summary>
    /// Unblock an author (204, idempotent). A missing row still succeeds:
    /// unblock is the caller's own action and never reveals whether a block
    /// existed. Removing the block restores the author's posts to the caller.
    /// </summary>
    [HttpDelete("{authorId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnblockAuthor(
        Guid authorId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new UnblockCommunityAuthorCommand(userId, new UserId(authorId)),
            cancellationToken);

        return ToCommunityBlockActionResult(result);
    }

    /// <summary>
    /// The caller's own blocked authors, most-recent block first, paged.
    /// Per viewer: never lists an author blocked by anyone else.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(
        typeof(IReadOnlyList<BlockedCommunityAuthorResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBlockedAuthors(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new GetBlockedCommunityAuthorsQuery(userId, page, pageSize),
            cancellationToken);

        return ToActionResult(result);
    }

    /// <summary>
    /// The unknown-target answer is forced to the generic 404 body here
    /// instead of editing the shared ResultProblemDetailsMapper (which
    /// parallel lanes also touch). The detail text is generic; the body's own
    /// error code stays intact so a client can key on it. The status pair
    /// still separates a known target (204) from an unknown one (404).
    /// </summary>
    private IActionResult ToCommunityBlockActionResult(Result result) =>
        result.IsFailure &&
        result.Error.Code == CommunityBlockErrors.NotFound.Code
            ? PrivacyNotFound(result.Error)
            : ToActionResult(result);

    private IActionResult PrivacyNotFound(Error error)
    {
        ProblemDetails problemDetails =
            ResultProblemDetailsMapper.Create(error, HttpContext);

        problemDetails.Type = "https://httpstatuses.com/404";
        problemDetails.Title = "Not Found";
        problemDetails.Status = StatusCodes.Status404NotFound;
        problemDetails.Detail = "The requested resource was not found.";

        return StatusCode(
            StatusCodes.Status404NotFound,
            problemDetails);
    }
}
