using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.Modules.CareHomes.Application.Bookings;

namespace Sanad.API.Controllers;

public sealed record CareHomeBookingDecisionRequest(bool Accept, string? Reason);
public sealed record CareHomeAssignmentRequest(Guid RoomId, Guid? BedId);
public sealed record CareHomeTransferRequest(Guid RoomId, Guid? BedId, DateOnly EffectiveDate);
public sealed record CareHomeBookingCancellationRequest(string Reason);

[Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
[Route("api/v1/care-homes/booking-requests/mine")]
public sealed class CareHomeBookingRequestsController(ISender sender, IDateTimeProvider clock) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => TryGetAuthenticatedUserId(out UserId actor) ? ToActionResult(await sender.Send(new ListOwnerCareHomeBookingsQuery(actor, clock.UtcNow), ct)) : Unauthorized();

    [HttpPost("{bookingId:guid}/decision")]
    public async Task<IActionResult> Decide(Guid bookingId, CareHomeBookingDecisionRequest request, CancellationToken ct) => TryGetAuthenticatedUserId(out UserId actor) ? ToActionResult(await sender.Send(new DecideCareHomeBookingCommand(actor, bookingId, request.Accept, request.Reason, clock.UtcNow), ct)) : Unauthorized();

    [HttpPost("{bookingId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid bookingId, CareHomeBookingCancellationRequest request, CancellationToken ct) => TryGetAuthenticatedUserId(out UserId actor) ? ToActionResult(await sender.Send(new CancelOwnerCareHomeBookingCommand(actor, bookingId, request.Reason, clock.UtcNow), ct)) : Unauthorized();

    [HttpPost("{bookingId:guid}/assignment")]
    public async Task<IActionResult> Assign(Guid bookingId, CareHomeAssignmentRequest request, CancellationToken ct) => TryGetAuthenticatedUserId(out UserId actor) ? ToActionResult(await sender.Send(new AssignCareHomeBookingCommand(actor, bookingId, request.RoomId, request.BedId, clock.UtcNow), ct)) : Unauthorized();

    [HttpPost("{bookingId:guid}/assignment/transfer")]
    public async Task<IActionResult> Transfer(Guid bookingId, CareHomeTransferRequest request, CancellationToken ct) => TryGetAuthenticatedUserId(out UserId actor) ? ToActionResult(await sender.Send(new TransferCareHomeBookingCommand(actor, bookingId, request.RoomId, request.BedId, request.EffectiveDate, clock.UtcNow), ct)) : Unauthorized();

    [HttpPost("{bookingId:guid}/check-in")]
    public async Task<IActionResult> CheckIn(Guid bookingId, CancellationToken ct) => TryGetAuthenticatedUserId(out UserId actor) ? ToActionResult(await sender.Send(new RecordCareHomeCheckInCommand(actor, bookingId, clock.UtcNow), ct)) : Unauthorized();

    [HttpPost("{bookingId:guid}/check-out")]
    public async Task<IActionResult> CheckOut(Guid bookingId, CancellationToken ct) => TryGetAuthenticatedUserId(out UserId actor) ? ToActionResult(await sender.Send(new RecordCareHomeCheckOutCommand(actor, bookingId, clock.UtcNow), ct)) : Unauthorized();

    [HttpGet("{bookingId:guid}/operational")]
    public async Task<IActionResult> Operational(Guid bookingId, CancellationToken ct) => TryGetAuthenticatedUserId(out UserId actor) ? ToActionResult(await sender.Send(new GetOwnerCareHomeBookingOperationalQuery(actor, bookingId, clock.UtcNow), ct)) : Unauthorized();

    [HttpGet("{bookingId:guid}/receipt")]
    public async Task<IActionResult> Receipt(Guid bookingId, CancellationToken ct) => TryGetAuthenticatedUserId(out UserId actor)
        ? ToActionResult(await sender.Send(new GetOwnerCareHomeReceiptQuery(actor, bookingId), ct)) : Unauthorized();

    [HttpGet("{bookingId:guid}/internal-notes")]
    public async Task<IActionResult> Notes(Guid bookingId, CancellationToken ct) => TryGetAuthenticatedUserId(out UserId actor)
        ? ToActionResult(await sender.Send(new GetOwnerCareHomeInternalBookingNotesQuery(actor, bookingId), ct)) : Unauthorized();

    [HttpPost("{bookingId:guid}/internal-notes")]
    public async Task<IActionResult> AddNote(Guid bookingId, [FromBody] AddCareHomeInternalNoteRequest request, CancellationToken ct) =>
        TryGetAuthenticatedUserId(out UserId actor)
            ? ToActionResult(await sender.Send(new AddOwnerCareHomeInternalBookingNoteCommand(actor, bookingId, request.Text, clock.UtcNow), ct))
            : Unauthorized();
}

public sealed record AddCareHomeInternalNoteRequest(string Text);
