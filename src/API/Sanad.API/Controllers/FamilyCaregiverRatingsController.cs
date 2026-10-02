using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Discovery;

namespace Sanad.API.Controllers;

public sealed record RateCaregiverRequest(int Stars, string? ReviewText);

[Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
[Route("api/v1/family/caregiver-ratings")]
public sealed class FamilyCaregiverRatingsController : ApiControllerBase
{
    private readonly ISender _sender;

    public FamilyCaregiverRatingsController(ISender sender) => _sender = sender;

    [HttpGet("top-10")]
    [ProducesResponseType(typeof(IReadOnlyList<TopRatedCaregiverCard>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopRatedCaregivers(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetTopRatedCaregiversQuery(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("bookings/{bookingId:guid}")]
    [ProducesResponseType(typeof(CaregiverRatingResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RateCompletedBooking(
        Guid bookingId,
        [FromBody] RateCaregiverRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();

        var result = await _sender.Send(
            new RateCompletedCaregiverBookingCommand(
                bookingId,
                actorUserId,
                request.Stars,
                request.ReviewText,
                DateTime.UtcNow),
            cancellationToken);

        return ToActionResult(result);
    }
}
