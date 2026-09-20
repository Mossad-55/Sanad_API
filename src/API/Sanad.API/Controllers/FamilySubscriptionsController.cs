using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Subscriptions;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
[Route("api/v1/family/subscriptions")]
public sealed class FamilySubscriptionsController : ApiControllerBase
{
    private readonly ISender _sender;

    public FamilySubscriptionsController(ISender sender) => _sender = sender;

    [HttpGet("plans")]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriptionPlanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPlans(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
            return Unauthorized();

        return ToActionResult(await _sender.Send(new GetSubscriptionCatalogQuery(userId), cancellationToken));
    }

    [HttpGet("current")]
    [ProducesResponseType(typeof(CurrentSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
            return Unauthorized();

        return ToActionResult(await _sender.Send(new GetCurrentSubscriptionQuery(userId), cancellationToken));
    }
}
