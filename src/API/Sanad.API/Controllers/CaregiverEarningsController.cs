using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Earnings;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CaregiverAccess)]
[Route("api/v1/caregivers")]
public sealed class CaregiverEarningsController : ApiControllerBase
{
    private readonly ISender _sender;

    public CaregiverEarningsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("{caregiverId:guid}/earnings/summary")]
    [ProducesResponseType(
        typeof(CaregiverEarningsSummaryResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEarningsSummary(
        Guid caregiverId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var query = new GetCaregiverEarningsSummaryQuery(caregiverId, userId);
        var result = await _sender.Send(query, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{caregiverId:guid}/earnings/transactions")]
    [ProducesResponseType(
        typeof(CaregiverEarningsTransactionResponse[]),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEarningsTransactions(
        Guid caregiverId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var query = new GetCaregiverEarningsTransactionsQuery(caregiverId, userId, page, pageSize);
        var result = await _sender.Send(query, cancellationToken);
        return ToActionResult(result);
    }
}
