using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.API.Controllers;

public sealed record CreateBookingCheckoutRequest(
    Guid ElderlyId,
    Guid CaregiverId,
    CaregiverType CaregiverType,
    BookingShiftType ShiftType,
    DateOnly BookingDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string ServiceAddress,
    string? SpecialInstructions,
    decimal BaseCaregiverFee);

public sealed record CreatePaymentIntentRequest(
    PaymentMethod Method,
    PaymobBillingData Billing);

public sealed record CancelBookingRequest(
    string Reason);

[Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
[Route("api/v1/family/bookings")]
public sealed class FamilyBookingsController : ApiControllerBase
{
    private readonly ISender _sender;
    private readonly IDateTimeProvider _dateTimeProvider;

    public FamilyBookingsController(
        ISender sender,
        IDateTimeProvider dateTimeProvider)
    {
        _sender = sender;
        _dateTimeProvider = dateTimeProvider;
    }

    [HttpGet]
    [ProducesResponseType(
        typeof(IReadOnlyList<FamilyBookingListItemResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBookings(
        [FromQuery] BookingTab tab = BookingTab.Upcoming,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var query = new GetFamilyBookingsQuery(userId, tab);
        var result = await _sender.Send(query, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{bookingId:guid}")]
    [ProducesResponseType(
        typeof(BookingDetailResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBookingDetail(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var query = new GetFamilyBookingDetailQuery(userId, new BookingId(bookingId));
        var result = await _sender.Send(query, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("checkout")]
    [ProducesResponseType(
        typeof(BookingCheckoutResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateCheckout(
        [FromBody] CreateBookingCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        DateTime utcNow = _dateTimeProvider.UtcNow;
        DateOnly currentDate = DateOnly.FromDateTime(utcNow);

        var command = new CreateBookingCheckoutCommand(
            userId,
            new ElderlyId(request.ElderlyId),
            new CaregiverId(request.CaregiverId),
            request.ShiftType,
            request.BookingDate,
            request.StartTime,
            request.EndTime,
            request.ServiceAddress,
            request.SpecialInstructions,
            currentDate,
            utcNow);

        var result = await _sender.Send(command, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{bookingId:guid}/cancel")]
    [ProducesResponseType(
        StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelBooking(
        Guid bookingId,
        [FromBody] CancelBookingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var command = new CancelBookingCommand(
            new BookingId(bookingId),
            userId,
            request.Reason,
            _dateTimeProvider.UtcNow);

        var result = await _sender.Send(command, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{bookingId:guid}/payments/intent")]
    [ProducesResponseType(
        typeof(BookingPaymentIntentResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> CreatePaymentIntent(
        Guid bookingId,
        [FromBody] CreatePaymentIntentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var command = new CreateBookingPaymentIntentCommand(
            new BookingId(bookingId),
            userId,
            request.Method,
            request.Billing,
            _dateTimeProvider.UtcNow);

        var result = await _sender.Send(command, cancellationToken);

        return ToActionResult(result);
    }
}