using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.PayoutAccounts;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CaregiverAccess)]
[Route("api/v1/caregiver/payout-account")]
public sealed class CaregiverPayoutAccountController : ApiControllerBase
{
    private readonly ISender _sender;
    private readonly ICaregiversDbContext _caregiversDb;

    public CaregiverPayoutAccountController(
        ISender sender,
        ICaregiversDbContext caregiversDb)
    {
        _sender = sender;
        _caregiversDb = caregiversDb;
    }

    [HttpGet]
    [ProducesResponseType(
        typeof(CaregiverPayoutAccountResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayoutAccount(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var caregiver = await _caregiversDb.Caregivers
            .SingleOrDefaultAsync(
                c => c.UserId == userId,
                cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new GetCaregiverPayoutAccountQuery(
                caregiver.Id.Value,
                userId),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut]
    [ProducesResponseType(
        typeof(CaregiverPayoutAccountResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePayoutAccount(
        [FromBody] UpdatePayoutAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var caregiver = await _caregiversDb.Caregivers
            .SingleOrDefaultAsync(
                c => c.UserId == userId,
                cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new UpdateCaregiverPayoutAccountCommand(
                caregiver.Id.Value,
                userId,
                request.AccountHolderName,
                request.BankCode,
                request.Iban),
            cancellationToken);

        return ToActionResult(result);
    }
}
