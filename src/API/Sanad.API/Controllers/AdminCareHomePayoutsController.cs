using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Bookings;

namespace Sanad.API.Controllers;

public sealed record RecordCareHomePayoutRequest(string TransferReference, string Evidence, string Reason);
public sealed record RecordCareHomePayoutReversalRequest(decimal CustomerRefundAmount, string Reference, string Reason);

[Authorize(Policy = AuthorizationPolicies.PayoutOperationalAdmin)]
[Route("api/v1/admin/care-homes/payouts")]
public sealed class AdminCareHomePayoutsController(ISender sender, IDateTimeProvider clock) : ApiControllerBase
{
    [HttpGet("ledger")]
    public async Task<IActionResult> Ledger([FromQuery] Guid? facilityId, CancellationToken ct) =>
        ToActionResult(await sender.Send(new GetCareHomePayoutLedgerQuery(facilityId, clock.UtcNow), ct));

    [HttpPost("bookings/{bookingId:guid}/record-reversal")]
    public async Task<IActionResult> RecordReversal(Guid bookingId, RecordCareHomePayoutReversalRequest request, CancellationToken ct) =>
        TryGetAuthenticatedUserId(out UserId actor)
            ? ToActionResult(await sender.Send(new RecordCareHomePayoutReversalCommand(
                actor, bookingId, request.CustomerRefundAmount, request.Reference, request.Reason, clock.UtcNow), ct))
            : Unauthorized();

    [HttpPost("bookings/{bookingId:guid}/record")]
    public async Task<IActionResult> Record(Guid bookingId, RecordCareHomePayoutRequest request, CancellationToken ct) =>
        TryGetAuthenticatedUserId(out UserId actor)
            ? ToActionResult(await sender.Send(new RecordCareHomePayoutCommand(
                actor, bookingId, request.TransferReference, request.Evidence, request.Reason, clock.UtcNow), ct))
            : Unauthorized();
}
