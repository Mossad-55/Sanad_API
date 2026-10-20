using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Discovery;

namespace Sanad.API.Controllers;

public sealed record ModerateCareHomeRatingRequest(string? Reason);

/// <summary>
/// F7 Care Home-rating moderation. Uses the existing Care Homes operational
/// policy (normal SuperAdmin + SupportAdmin) so no policy or DI file changes;
/// the pinned status mapping lives in the shared problem-details mapper. Hide,
/// keep and restore return 204, and hiding/keeping resolve the rating's
/// unresolved owner flag with attribution.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.CareHomesOperationalAdmin)]
[Route("api/v1/admin/care-homes/ratings")]
public sealed class AdminCareHomeRatingsController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AdminCareHomeRatingPage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? facilityId = null,
        [FromQuery] int? stars = null,
        [FromQuery] string? moderation = null,
        [FromQuery] bool? flagged = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await sender.Send(
            new GetAdminCareHomeRatingsQuery(facilityId, stars, moderation, flagged, page, pageSize),
            cancellationToken));

    [HttpPost("{ratingId:guid}/hide")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Hide(
        Guid ratingId,
        [FromBody] ModerateCareHomeRatingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();

        var result = await sender.Send(
            new HideAdminCareHomeRatingCommand(ratingId, actorUserId, request.Reason, DateTime.UtcNow),
            cancellationToken);

        return result.IsSuccess ? NoContent() : ToActionResult(Result.Failure(result.Error));
    }

    [HttpPost("{ratingId:guid}/keep")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Keep(
        Guid ratingId,
        [FromBody] ModerateCareHomeRatingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();

        var result = await sender.Send(
            new KeepAdminCareHomeRatingCommand(ratingId, actorUserId, request.Reason, DateTime.UtcNow),
            cancellationToken);

        return result.IsSuccess ? NoContent() : ToActionResult(Result.Failure(result.Error));
    }

    [HttpPost("{ratingId:guid}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(
        Guid ratingId,
        [FromBody] ModerateCareHomeRatingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();

        var result = await sender.Send(
            new RestoreAdminCareHomeRatingCommand(ratingId, actorUserId, request.Reason, DateTime.UtcNow),
            cancellationToken);

        return result.IsSuccess ? NoContent() : ToActionResult(Result.Failure(result.Error));
    }
}
