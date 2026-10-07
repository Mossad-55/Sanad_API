using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Bookings;

namespace Sanad.API.Controllers;

public sealed record RecordCompletedCareHomeRefundRequest(string Reference, string Reason);

[Authorize(Policy = AuthorizationPolicies.CareHomesOperationalAdmin)]
[Route("api/v1/admin/care-homes/bookings/{bookingId:guid}/refund")]
public sealed class AdminCareHomeRefundsController(ISender sender, IDateTimeProvider clock) : ApiControllerBase
{
    [HttpPost("retry")]
    public async Task<IActionResult> Retry(Guid bookingId, CancellationToken ct) =>
        TryGetAuthenticatedUserId(out UserId actor)
            ? ToActionResult(await sender.Send(new RetryCareHomeRefundCommand(actor, bookingId, clock.UtcNow), ct))
            : Unauthorized();

    [HttpPost("record-completed")]
    public async Task<IActionResult> RecordCompleted(
        Guid bookingId,
        RecordCompletedCareHomeRefundRequest request,
        CancellationToken ct) =>
        TryGetAuthenticatedUserId(out UserId actor)
            ? ToActionResult(await sender.Send(new RecordCareHomeRefundCompletedCommand(
                actor, bookingId, request.Reference, request.Reason, clock.UtcNow), ct))
            : Unauthorized();
}
