using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Activities;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
[Route("api/v1/family/dependents/{dependentId:guid}/activities")]
public sealed class ElderlyActivitiesController : ApiControllerBase
{
    private readonly ISender _sender;

    public ElderlyActivitiesController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ElderlyActivityDashboardResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActivityTimeline(
        Guid dependentId,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        var result = await _sender.Send(new GetElderlyActivityTimelineQuery(
            userId,
            new ElderlyId(dependentId),
            limit), cancellationToken);

        return ToActionResult(result);
    }
}