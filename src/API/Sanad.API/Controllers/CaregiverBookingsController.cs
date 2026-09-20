using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Authorization;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Application.Reports;
using Sanad.Modules.Families.Domain.Reports;

namespace Sanad.API.Controllers;

public sealed record DeclineBookingRequest(string Reason);
public sealed record CompleteBookingRequest(string? Notes);
public sealed record CaregiverCancelBookingRequest(string? Reason, int? ReasonCategory);
public sealed record SubmitVisitReportRequest(
    string? ObservedCondition,
    string? Activities,
    string? Notes,
    VisitReportAssessment Assessment);

[Authorize(Policy = AuthorizationPolicies.CaregiverAccess)]
[Route("api/v1/caregiver/bookings")]
public sealed class CaregiverBookingsController : ApiControllerBase
{
    private readonly ISender _sender;
    private readonly ICaregiversDbContext _caregiversDb;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CaregiverBookingsController(
        ISender sender,
        ICaregiversDbContext caregiversDb,
        IDateTimeProvider dateTimeProvider)
    {
        _sender = sender;
        _caregiversDb = caregiversDb;
        _dateTimeProvider = dateTimeProvider;
    }

    [HttpGet]
    [ProducesResponseType(
        typeof(IReadOnlyList<CaregiverBookingListItemResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBookings(
        [FromQuery] BookingTab tab = BookingTab.Upcoming,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var caregiver = await _caregiversDb.Caregivers
            .SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new GetCaregiverBookingsQuery(caregiver.Id, tab),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{bookingId:guid}")]
    [ProducesResponseType(typeof(BookingDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBookingDetail(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var caregiver = await _caregiversDb.Caregivers
            .SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new GetCaregiverBookingDetailQuery(
                caregiver.Id,
                new BookingId(bookingId)),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{bookingId:guid}/accept")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> AcceptBooking(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var caregiver = await _caregiversDb.Caregivers
            .SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var command = new CaregiverAcceptBookingCommand(
            caregiver.Id,
            new BookingId(bookingId),
            _dateTimeProvider.UtcNow);

        var result = await _sender.Send(command, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{bookingId:guid}/decline")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeclineBooking(
        Guid bookingId,
        [FromBody] DeclineBookingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var caregiver = await _caregiversDb.Caregivers
            .SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var command = new CaregiverDeclineBookingCommand(
            caregiver.Id,
            userId,
            new BookingId(bookingId),
            request.Reason,
            _dateTimeProvider.UtcNow);

        var result = await _sender.Send(command, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{bookingId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelBooking(
        Guid bookingId,
        [FromBody] CaregiverCancelBookingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var caregiver = await _caregiversDb.Caregivers
            .SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var command = new CaregiverCancelBookingCommand(
            caregiver.Id,
            userId,
            new BookingId(bookingId),
            request.Reason,
            request.ReasonCategory,
            _dateTimeProvider.UtcNow);

        var result = await _sender.Send(command, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{bookingId:guid}/start")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> StartVisit(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var caregiver = await _caregiversDb.Caregivers
            .SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var command = new CaregiverStartBookingCommand(
            caregiver.Id,
            new BookingId(bookingId),
            _dateTimeProvider.UtcNow);

        var result = await _sender.Send(command, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{bookingId:guid}/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CompleteVisit(
        Guid bookingId,
        [FromBody] CompleteBookingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var caregiver = await _caregiversDb.Caregivers
            .SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var command = new CaregiverCompleteBookingCommand(
            caregiver.Id,
            new BookingId(bookingId),
            request.Notes,
            _dateTimeProvider.UtcNow);

        var result = await _sender.Send(command, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{bookingId:guid}/visit-report")]
    [ProducesResponseType(typeof(VisitReportResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitVisitReport(
        Guid bookingId,
        [FromBody] SubmitVisitReportRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var caregiver = await _caregiversDb.Caregivers
            .SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new SubmitVisitReportCommand(
                caregiver.Id,
                userId,
                new BookingId(bookingId),
                request.ObservedCondition,
                request.Activities,
                request.Notes,
                request.Assessment,
                _dateTimeProvider.UtcNow),
            cancellationToken);

        if (result.IsSuccess)
        {
            return StatusCode(StatusCodes.Status201Created, result.Value);
        }

        return ToVisitReportActionResult(result.Error);
    }

    private IActionResult ToVisitReportActionResult(Error error)
    {
        ProblemDetails problemDetails = ResultProblemDetailsMapper.Create(error, HttpContext);
        int statusCode = error.Code switch
        {
            "Reports.Visit.InvalidContent" => StatusCodes.Status400BadRequest,
            "Reports.Visit.AlreadySubmitted" => StatusCodes.Status409Conflict,
            "Bookings.NotFound" => StatusCodes.Status404NotFound,
            "Bookings.Domain.InvalidOperation" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        problemDetails.Status = statusCode;
        return StatusCode(statusCode, problemDetails);
    }
}
