using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Discovery;

namespace Sanad.API.Controllers;

public sealed record FlagCareHomeRatingRequest(string? Reason);

/// <summary>
/// F7 owner facility ratings. The Care Home owner sees the ratings about their
/// own facility anonymously — the reviewer is never identified — excludes
/// hidden ratings from the page and both aggregates, and may flag a rating
/// (204, at most one unresolved flag per rating). Owners cannot hide, restore
/// or keep, and cannot see who reviewed.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
[Route("api/v1/care-homes/facilities/mine/ratings")]
public sealed class OwnerCareHomeRatingsController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(OwnerCareHomeRatingPage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new GetOwnerCareHomeRatingsQuery(actorUserId, page, pageSize),
            cancellationToken));
    }

    [HttpPost("{ratingId:guid}/flag")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Flag(
        Guid ratingId,
        [FromBody] FlagCareHomeRatingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();

        var result = await sender.Send(
            new FlagOwnerCareHomeRatingCommand(actorUserId, ratingId, request.Reason, DateTime.UtcNow),
            cancellationToken);

        return result.IsSuccess ? NoContent() : ToActionResult(Result.Failure(result.Error));
    }
}
