using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Availability;

namespace Sanad.API.Controllers;

[Authorize(Policy = "FamilyAccess")]
[Route("api/v1/caregivers")]
public sealed class CaregiverAvailabilityController : ApiControllerBase
{
    private readonly ISender _sender;

    public CaregiverAvailabilityController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("{caregiverId:guid}/availability")]
    [ProducesResponseType(
        typeof(CaregiverAvailabilityResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailability(
        Guid caregiverId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var query = new GetCaregiverAvailabilityQuery(caregiverId);
        var result = await _sender.Send(query, cancellationToken);
        return ToActionResult(result);
    }
}