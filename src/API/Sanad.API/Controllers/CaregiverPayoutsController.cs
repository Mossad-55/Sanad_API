using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Application.Payouts;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.API.Controllers;

/// <summary>
/// Caregiver-facing payout reads (plan.md §4). Self-only: the caregiver is
/// resolved from the authenticated identity, so callers can never read
/// another caregiver's payouts.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.CaregiverAccess)]
[Route("api/v1/caregiver")]
public sealed class CaregiverPayoutsController(
    ISender sender,
    ICaregiversDbContext caregiversDb) : ApiControllerBase
{
    [HttpGet("payouts/summary")]
    [ProducesResponseType(
        typeof(CaregiverPayoutSummaryResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver = await ResolveCaregiverAsync(cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var result = await sender.Send(
            new GetCaregiverPayoutSummaryQuery(caregiver.Id.Value),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("payouts")]
    [ProducesResponseType(
        typeof(PagedResult<CaregiverPayoutListItemResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayouts(
        [FromQuery] PayoutStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        Caregiver? caregiver = await ResolveCaregiverAsync(cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var result = await sender.Send(
            new GetCaregiverPayoutListQuery(
                caregiver.Id.Value,
                status,
                page,
                pageSize),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("payouts/{payoutId:guid}")]
    [ProducesResponseType(
        typeof(CaregiverPayoutDetailResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayout(
        Guid payoutId,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver = await ResolveCaregiverAsync(cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var result = await sender.Send(
            new GetCaregiverPayoutDetailQuery(
                caregiver.Id.Value,
                payoutId),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("payment-policy")]
    [ProducesResponseType(
        typeof(CaregiverPaymentPolicyResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaymentPolicy(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out _))
        {
            return Unauthorized();
        }

        var result = await sender.Send(
            new GetCaregiverPaymentPolicyQuery(),
            cancellationToken);

        return ToActionResult(result);
    }

    private async Task<Caregiver?> ResolveCaregiverAsync(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return null;
        }

        return await caregiversDb.Caregivers
            .SingleOrDefaultAsync(
                c => c.UserId == userId,
                cancellationToken);
    }
}
