using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.Modules.Finance.Application;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.PayoutOperationalAdmin)]
[Route("api/v1/admin/finance/caregiver-payout-policies")]
public sealed class AdminCaregiverPayoutPolicyController(ISender sender) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateCaregiverPayoutPolicyRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new CreateCaregiverPayoutPolicyCommand(request.PayoutDelayHours, request.Version, request.EffectiveOnUtc), ct);
        if (result.IsSuccess) return StatusCode(StatusCodes.Status201Created, result.Value);
        return result.Error.Code == "Finance.PayoutPolicy.Invalid"
            ? BadRequest(result.Error)
            : Conflict(result.Error);
    }

    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken ct) => Ok((await sender.Send(new GetCurrentCaregiverPayoutPolicyQuery(), ct)).Value);

    [HttpGet("history")]
    public async Task<IActionResult> History(CancellationToken ct) => Ok((await sender.Send(new GetCaregiverPayoutPolicyHistoryQuery(), ct)).Value);
}

public sealed record CreateCaregiverPayoutPolicyRequest(int PayoutDelayHours, int Version, DateTime EffectiveOnUtc);
