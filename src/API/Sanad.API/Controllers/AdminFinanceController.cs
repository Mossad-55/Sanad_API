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
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreatePlatformChargeRuleRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new CreatePlatformChargeRuleCommand(request.PlatformFeeRatePercentage, request.TaxRatePercentage, request.Version, request.EffectiveOnUtc), ct);
        if (result.IsSuccess) return StatusCode(StatusCodes.Status201Created, result.Value);
        return result.Error.Code == "Finance.Charges.Invalid"
            ? BadRequest(result.Error)
            : Conflict(result.Error);
    }

    [HttpGet("platform-charge-rules/current")]
    [ProducesResponseType(typeof(PlatformChargeRuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Current(CancellationToken ct)
    {
        PlatformChargeRuleResponse? current =
            (await sender.Send(new GetCurrentPlatformChargeRuleQuery(), ct)).Value;

        return current is null ? NoContent() : Ok(current);
    }

    [HttpGet("platform-charge-rules/history")]
    [ProducesResponseType(typeof(IReadOnlyList<PlatformChargeRuleResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> History(CancellationToken ct) => Ok((await sender.Send(new GetPlatformChargeRuleHistoryQuery(), ct)).Value);
}

public sealed record CreatePlatformChargeRuleRequest(decimal PlatformFeeRatePercentage, decimal TaxRatePercentage, int Version, DateTime EffectiveOnUtc);
