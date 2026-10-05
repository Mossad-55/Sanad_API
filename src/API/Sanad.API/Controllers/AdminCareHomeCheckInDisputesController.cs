using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Bookings;

namespace Sanad.API.Controllers;

public sealed record ResolveCareHomeCheckInDisputeRequest(DateTime EffectiveCheckInOnUtc, string Evidence, string Reason);

[Authorize(Policy = AuthorizationPolicies.CareHomesOperationalAdmin)]
[Route("api/v1/admin/care-homes/check-in-disputes")]
public sealed class AdminCareHomeCheckInDisputesController(ISender sender, IDateTimeProvider clock) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => ToActionResult(await sender.Send(new ListCareHomeCheckInDisputesQuery(), ct));

    [HttpPost("{caseId:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid caseId, ResolveCareHomeCheckInDisputeRequest request, CancellationToken ct) => TryGetAuthenticatedUserId(out UserId actor) ? ToActionResult(await sender.Send(new ResolveCareHomeCheckInDisputeCommand(actor, caseId, request.EffectiveCheckInOnUtc, request.Evidence, request.Reason, clock.UtcNow), ct)) : Unauthorized();
}
