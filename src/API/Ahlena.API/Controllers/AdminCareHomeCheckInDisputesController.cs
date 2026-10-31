using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Application.Bookings;

namespace Ahlena.API.Controllers;

public sealed record ResolveCareHomeCheckInDisputeRequest(DateTime EffectiveCheckInOnUtc, string Evidence, string Reason);

[Authorize(Policy = AuthorizationPolicies.CareHomesOperationalAdmin)]
[Route("api/v1/admin/care-homes/check-in-disputes")]
public sealed class AdminCareHomeCheckInDisputesController(ISender sender, IDateTimeProvider clock) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminCareHomeCheckInDispute>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => ToActionResult(await sender.Send(new ListCareHomeCheckInDisputesQuery(), ct));

    [HttpPost("{caseId:guid}/resolve")]
    [ProducesResponseType(typeof(CareHomeCheckInDisputeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resolve(Guid caseId, ResolveCareHomeCheckInDisputeRequest request, CancellationToken ct) => TryGetAuthenticatedUserId(out UserId actor) ? ToActionResult(await sender.Send(new ResolveCareHomeCheckInDisputeCommand(actor, caseId, request.EffectiveCheckInOnUtc, request.Evidence, request.Reason, clock.UtcNow), ct)) : Unauthorized();
}
