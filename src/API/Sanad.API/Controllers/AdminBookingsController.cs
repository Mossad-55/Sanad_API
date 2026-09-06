using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Bookings;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CaregiversAdmin)]
[Route("api/v1/admin/bookings")]
public sealed class AdminBookingsController : ApiControllerBase
{
    private readonly ISender _sender;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AdminBookingsController(
        ISender sender,
        IDateTimeProvider dateTimeProvider)
    {
        _sender = sender;
        _dateTimeProvider = dateTimeProvider;
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

    [HttpPost("{bookingId:guid}/refund")]
    [ProducesResponseType(typeof(BookingDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RefundBooking(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new AdminRefundBookingCommand(
                new BookingId(bookingId),
                _dateTimeProvider.UtcNow),
            cancellationToken);

        return ToActionResult(result);
    }
}
