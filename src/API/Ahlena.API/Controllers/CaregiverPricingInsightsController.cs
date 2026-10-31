using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Caregivers.Application.Discovery;

namespace Ahlena.API.Controllers;

/// <summary>
/// Chat Plus pricing insights: market hourly range and eight-hour day range
/// for one booked area/service pair, served in the exact frontend shape.
/// Normal authenticated caregiver access; both ids required. Insufficient
/// data returns the relevant range as null — never invented numbers or
/// zero ranges. Calculation instants, windows, pools, and attribution are
/// pinned by <see cref="GetPricingInsightsQuery"/>.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.CaregiverAccess)]
[Route("api/v1/caregiver/pricing-insights")]
public sealed class CaregiverPricingInsightsController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(
        typeof(PricingInsightsResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPricingInsights(
        [FromQuery] Guid areaId,
        [FromQuery] Guid serviceId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId _))
        {
            return Unauthorized();
        }

        var result = await sender.Send(
            new GetPricingInsightsQuery(areaId, serviceId),
            cancellationToken);

        return ToActionResult(result);
    }
}
