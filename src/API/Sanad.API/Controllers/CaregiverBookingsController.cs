using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Bookings;

namespace Sanad.API.Controllers;

public sealed record DeclineBookingRequest(string Reason);
public sealed record CompleteBookingRequest(string? Notes);

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
            new BookingId(bookingId),
            request.Reason,
            _dateTimeProvider.UtcNow);

        var result = await _sender.Send(command, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{bookingId:guid}/start")]
    [ProducesResponseType(StatusCodes.Status200OK)]
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
    [ProducesResponseType(StatusCodes.Status200OK)]
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
}