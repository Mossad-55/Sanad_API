using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Discovery;
using Sanad.Modules.Families.Application.Abstractions.Data;

namespace Sanad.API.Controllers;

public sealed record RateCareHomeRequest(int Stars, string? ReviewText);

[Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
[Route("api/v1/family/care-home-ratings")]
public sealed class FamilyCareHomeRatingsController(ISender sender, IFamiliesDbContext families,
    IDateTimeProvider clock) : ApiControllerBase
{
    [HttpGet("top-10")]
    [ProducesResponseType(typeof(IReadOnlyList<TopRatedCareHomeCard>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Top10(CancellationToken ct) =>
        ToActionResult(await sender.Send(new GetTopRatedCareHomesQuery(), ct));

    [HttpPut("bookings/{bookingId:guid}")]
    [ProducesResponseType(typeof(CareHomeRatingResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Rate(Guid bookingId, RateCareHomeRequest request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor)) return Unauthorized();
        var family = await families.Families.AsNoTracking().SingleOrDefaultAsync(x => x.DeletedOnUtc == null &&
            (x.OwnerUserId == actor || x.Members.Any(member => member.Id == actor)), ct);
        if (family is null) return NotFound();
        return ToActionResult(await sender.Send(new RateCareHomeBookingCommand(
            bookingId, actor, family.Id, request.Stars, request.ReviewText, clock.UtcNow), ct));
    }
}
