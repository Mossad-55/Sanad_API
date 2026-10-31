using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Caregivers.Application.Discovery;

namespace Ahlena.API.Controllers;

public sealed record ModerateCaregiverRatingRequest(string? Reason);

/// <summary>
/// F7 caregiver-rating moderation. Uses the existing caregiver review policy
/// (normal SuperAdmin + SupportAdmin) so no policy or DI file changes; the
/// pinned status mapping lives in the shared problem-details mapper. Hide and
/// restore return 204, exactly like the sibling moderation actions, and never
/// mutate the retained rating content.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.CaregiverReviewAdmin)]
[Route("api/v1/admin/caregivers/ratings")]
public sealed class AdminCaregiverRatingsController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AdminCaregiverRatingPage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? caregiverId = null,
        [FromQuery] int? stars = null,
        [FromQuery] string? moderation = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await sender.Send(
            new GetAdminCaregiverRatingsQuery(caregiverId, stars, moderation, page, pageSize),
            cancellationToken));

    [HttpPost("{ratingId:guid}/hide")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Hide(
        Guid ratingId,
        [FromBody] ModerateCaregiverRatingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();

        var result = await sender.Send(
            new HideAdminCaregiverRatingCommand(ratingId, actorUserId, request.Reason, DateTime.UtcNow),
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
        [FromBody] ModerateCaregiverRatingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();

        var result = await sender.Send(
            new RestoreAdminCaregiverRatingCommand(ratingId, actorUserId, request.Reason, DateTime.UtcNow),
            cancellationToken);

        return result.IsSuccess ? NoContent() : ToActionResult(Result.Failure(result.Error));
    }
}
