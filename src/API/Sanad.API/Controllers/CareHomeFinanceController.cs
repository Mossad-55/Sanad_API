using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Bookings;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
[Route("api/v1/care-homes")]
public sealed class CareHomeFinanceController(ISender sender, IDateTimeProvider clock) : ApiControllerBase
{
    [HttpGet("dashboard/mine")]
    public async Task<IActionResult> Dashboard([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        TryGetAuthenticatedUserId(out UserId actor)
            ? ToActionResult(await sender.Send(new GetOwnerCareHomeDashboardQuery(actor, from, to, clock.UtcNow), ct))
            : Unauthorized();

    [HttpGet("revenue/mine")]
    public async Task<IActionResult> Revenue([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        TryGetAuthenticatedUserId(out UserId actor)
            ? ToActionResult(await sender.Send(new GetOwnerCareHomeRevenueQuery(actor, from, to), ct))
            : Unauthorized();

    [HttpGet("revenue/mine/export.csv")]
    public async Task<IActionResult> Export([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor)) return Unauthorized();
        var result = await sender.Send(new GetOwnerCareHomeRevenueQuery(actor, from, to), ct);
        return result.IsFailure ? ToActionResult(result)
            : File(CareHomeRevenueCsv.Render(result.Value), "text/csv; charset=utf-8", "care-home-revenue.csv");
    }
}
