using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Rentals.Application;
using Sanad.Modules.Rentals.Domain;

namespace Sanad.API.Controllers;

public sealed record CreateRentalBookingRequest(
    Guid EquipmentId,
    int PlannedDays,
    string? DeliveryAddress);

public sealed record CreateRentalPaymentIntentRequest(
    RentalPaymentMethod Method,
    PaymobBillingData Billing);

/// <summary>
/// The family chooses only the destination and supplies the idempotency key;
/// the amount is always policy-derived inside the module (clause 8, #10, #19).
/// </summary>
public sealed record RentalRefundRequest(
    RentalRefundDestination Destination,
    string? IdempotencyKey = null);

public sealed record DisputeRentalDeductionRequest(string? Reason);

/// <summary>
/// The family surface of the equipment-rental lifecycle (refund contract
/// clause 8, Owner decisions #19-#22): browse the catalog with the exact
/// pre-purchase terms, book, pay the rent-plus-deposit total through Paymob
/// (the <c>rnt_</c> merchant reference confirms through the shared webhook),
/// cancel before delivery for a full refund, claim the unused whole prepaid
/// periods after an early return, and dispute a recorded damage deduction.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
[Route("api/v1/family/rentals")]
public sealed class FamilyRentalsController(ISender sender, IDateTimeProvider clock) : ApiControllerBase
{
    [HttpGet("catalog")]
    [ProducesResponseType(typeof(IReadOnlyList<RentalCatalogItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Catalog(CancellationToken cancellationToken)
    {
        return ToActionResult(await sender.Send(new GetRentalCatalogQuery(), cancellationToken));
    }

    [HttpGet("catalog/{equipmentId:guid}")]
    [ProducesResponseType(typeof(RentalCatalogItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CatalogItem(Guid equipmentId, CancellationToken cancellationToken)
    {
        return ToActionResult(await sender.Send(new GetRentalCatalogItemQuery(equipmentId), cancellationToken));
    }

    [HttpPost("bookings")]
    [ProducesResponseType(typeof(RentalBookingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateBooking(CreateRentalBookingRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new CreateRentalBookingCommand(actor.Value, request.EquipmentId, request.PlannedDays, request.DeliveryAddress, clock.UtcNow),
            cancellationToken));
    }

    [HttpGet("bookings")]
    [ProducesResponseType(typeof(IReadOnlyList<RentalBookingDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Bookings(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(new GetFamilyRentalBookingsQuery(actor.Value), cancellationToken));
    }

    [HttpGet("bookings/{rentalBookingId:guid}")]
    [ProducesResponseType(typeof(RentalBookingDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BookingDetail(Guid rentalBookingId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(new GetFamilyRentalBookingDetailQuery(actor.Value, rentalBookingId), cancellationToken));
    }

    [HttpPost("bookings/{rentalBookingId:guid}/payments/intent")]
    [ProducesResponseType(typeof(RentalPaymentIntentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PaymentIntent(Guid rentalBookingId, CreateRentalPaymentIntentRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new CreateRentalPaymentIntentCommand(rentalBookingId, actor.Value, request.Method, request.Billing, clock.UtcNow),
            cancellationToken));
    }

    [HttpPost("bookings/{rentalBookingId:guid}/cancel")]
    [ProducesResponseType(typeof(RentalBookingRefundResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid rentalBookingId, RentalRefundRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new CancelRentalBookingCommand(rentalBookingId, actor.Value, request.Destination, request.IdempotencyKey, clock.UtcNow),
            cancellationToken));
    }

    [HttpPost("bookings/{rentalBookingId:guid}/refunds/unused-days")]
    [ProducesResponseType(typeof(RentalBookingRefundResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ClaimUnusedDays(Guid rentalBookingId, RentalRefundRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new ClaimRentalUnusedDaysRefundCommand(rentalBookingId, actor.Value, request.Destination, request.IdempotencyKey, clock.UtcNow),
            cancellationToken));
    }

    [HttpPost("bookings/{rentalBookingId:guid}/deductions/{deductionId:guid}/dispute")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DisputeDeduction(Guid rentalBookingId, Guid deductionId, DisputeRentalDeductionRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new DisputeRentalDamageDeductionCommand(rentalBookingId, deductionId, actor.Value, request.Reason, clock.UtcNow),
            cancellationToken));
    }
}
