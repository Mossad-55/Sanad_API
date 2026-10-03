using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.Modules.Finance.Application;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.FinanceOperationalAdmin)]
[Route("api/v1/admin/finance")]
public sealed class AdminFinanceController(ISender sender) : ApiControllerBase
{
    [HttpPost("platform-charge-rules")]
    public async Task<IActionResult> Create(CreatePlatformChargeRuleRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new CreatePlatformChargeRuleCommand(request.PlatformFeeRatePercentage, request.TaxRatePercentage, request.Version, request.EffectiveOnUtc), ct);
        if (result.IsSuccess) return StatusCode(StatusCodes.Status201Created, result.Value);
        return result.Error.Code == "Finance.Charges.Invalid"
            ? BadRequest(result.Error)
            : Conflict(result.Error);
    }

    [HttpGet("platform-charge-rules/current")]
    public async Task<IActionResult> Current(CancellationToken ct) => Ok((await sender.Send(new GetCurrentPlatformChargeRuleQuery(), ct)).Value);

    [HttpGet("platform-charge-rules/history")]
    public async Task<IActionResult> History(CancellationToken ct) => Ok((await sender.Send(new GetPlatformChargeRuleHistoryQuery(), ct)).Value);
}

public sealed record CreatePlatformChargeRuleRequest(decimal PlatformFeeRatePercentage, decimal TaxRatePercentage, int Version, DateTime EffectiveOnUtc);
