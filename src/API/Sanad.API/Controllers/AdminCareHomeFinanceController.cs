using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Bookings;

namespace Sanad.API.Controllers;

public sealed record AddAdminCareHomeInternalNoteRequest(string Text);

[Authorize(Policy = AuthorizationPolicies.CareHomesOperationalAdmin)]
[Route("api/v1/admin/care-homes")]
public sealed class AdminCareHomeFinanceController(ISender sender, IDateTimeProvider clock) : ApiControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] Guid? facilityId, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, CancellationToken ct) =>
        ToActionResult(await sender.Send(new GetAdminCareHomeDashboardQuery(facilityId, from, to, clock.UtcNow), ct));

    [HttpGet("revenue")]
    public async Task<IActionResult> Revenue([FromQuery] Guid? facilityId, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, CancellationToken ct) =>
        ToActionResult(await sender.Send(new GetAdminCareHomeRevenueQuery(facilityId, from, to), ct));

    [HttpGet("revenue/export.csv")]
    public async Task<IActionResult> Export([FromQuery] Guid? facilityId, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var result = await sender.Send(new GetAdminCareHomeRevenueQuery(facilityId, from, to), ct);
        return result.IsFailure ? ToActionResult(result)
            : File(CareHomeRevenueCsv.Render(result.Value), "text/csv; charset=utf-8", "care-home-revenue.csv");
    }

    [HttpGet("bookings/{bookingId:guid}/receipt")]
    public async Task<IActionResult> Receipt(Guid bookingId, CancellationToken ct) =>
        ToActionResult(await sender.Send(new GetAdminCareHomeReceiptQuery(bookingId), ct));

    [HttpGet("bookings/{bookingId:guid}/internal-notes")]
    public async Task<IActionResult> Notes(Guid bookingId, CancellationToken ct) =>
        ToActionResult(await sender.Send(new GetAdminCareHomeInternalBookingNotesQuery(bookingId), ct));

    [HttpPost("bookings/{bookingId:guid}/internal-notes")]
    public async Task<IActionResult> AddNote(Guid bookingId, [FromBody] AddAdminCareHomeInternalNoteRequest request,
        CancellationToken ct) =>
        TryGetAuthenticatedUserId(out UserId actor)
            ? ToActionResult(await sender.Send(new AddAdminCareHomeInternalBookingNoteCommand(actor, bookingId, request.Text, clock.UtcNow), ct))
            : Unauthorized();
}
