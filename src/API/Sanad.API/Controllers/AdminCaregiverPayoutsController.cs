using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Application.Payouts;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.PayoutOperationalAdmin)]
[Route("api/v1/admin/caregiver-payouts")]
public sealed class AdminCaregiverPayoutsController(
    ISender sender,
    IDateTimeProvider clock) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(
        typeof(PagedResult<CaregiverPayoutResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayouts(
        [FromQuery] PayoutStatus? status,
        [FromQuery] Guid? caregiverId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new GetCaregiverPayoutsQuery(status, caregiverId, page, pageSize),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{payoutId:guid}")]
    [ProducesResponseType(
        typeof(CaregiverPayoutResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayout(
        Guid payoutId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetCaregiverPayoutQuery(payoutId),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("record")]
    [ProducesResponseType(
        typeof(CaregiverPayoutResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> Record(
        [FromBody] RecordCaregiverPayoutRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
        {
            return Unauthorized();
        }

        var result = await sender.Send(
            new RecordCaregiverPayoutCommand(
                request.BookingId,
                actor,
                request.TransferReference,
                request.Evidence,
                request.Reason,
                clock.UtcNow),
            cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        return StatusCode(
            StatusCodes.Status201Created,
            result.Value);
    }

    [HttpPost("{payoutId:guid}/mark-failed")]
    [ProducesResponseType(
        typeof(CaregiverPayoutResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkFailed(
        Guid payoutId,
        [FromBody] MarkCaregiverPayoutFailedRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
        {
            return Unauthorized();
        }

        var result = await sender.Send(
            new MarkCaregiverPayoutFailedCommand(
                payoutId,
                actor,
                request.Reason,
                clock.UtcNow),
            cancellationToken);

        return ToActionResult(result);
    }
}
