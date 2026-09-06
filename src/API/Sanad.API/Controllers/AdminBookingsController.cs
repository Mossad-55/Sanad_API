using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Bookings;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CaregiversAdmin)]
[Route("api/v1/admin/bookings")]
public sealed class AdminBookingsController : ApiControllerBase
{
    private readonly ISender _sender;

    public AdminBookingsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedAdminBookings), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListBookings(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] AdminBookingFinanceFilter finance = AdminBookingFinanceFilter.All,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new ListAdminBookingsQuery(page, pageSize, finance),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{bookingId:guid}")]
    [ProducesResponseType(typeof(BookingDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBooking(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetAdminBookingDetailQuery(new BookingId(bookingId)),
            cancellationToken);

        return ToActionResult(result);
    }
}
