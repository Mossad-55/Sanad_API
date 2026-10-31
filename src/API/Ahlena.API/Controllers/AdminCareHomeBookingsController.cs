using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.Modules.CareHomes.Application.Bookings;
using Ahlena.Modules.CareHomes.Domain.Bookings;

namespace Ahlena.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CareHomesOperationalAdmin)]
[Route("api/v1/admin/care-homes/bookings")]
public sealed class AdminCareHomeBookingsController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AdminCareHomeBookingPage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] Guid? facilityId = null, [FromQuery] CareHomeBookingStatus? bookingStatus = null,
        [FromQuery] CareHomeBookingPaymentStatus? paymentStatus = null,
        [FromQuery] CareHomeRefundStatus? refundStatus = null, [FromQuery] DateOnly? stayFrom = null,
        [FromQuery] DateOnly? stayTo = null, [FromQuery] string? search = null, CancellationToken ct = default) =>
        ToActionResult(await sender.Send(new ListAdminCareHomeBookingsQuery(page, pageSize, facilityId,
            bookingStatus, paymentStatus, refundStatus, stayFrom, stayTo, search), ct));

    [HttpGet("{bookingId:guid}")]
    [ProducesResponseType(typeof(AdminCareHomeBookingDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail(Guid bookingId, CancellationToken ct) =>
        ToActionResult(await sender.Send(new GetAdminCareHomeBookingQuery(bookingId), ct));
}
