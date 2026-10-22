using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Rentals.Application;
using Sanad.Modules.Rentals.Domain;

namespace Sanad.API.Controllers;

public sealed record CompleteRentalInspectionRequest(string? Notes);

public sealed record RecordRentalDamageDeductionRequest(
    string DescriptionEnglish,
    string? DescriptionArabic,
    long AmountMinorUnits,
    string EvidenceReference);

public sealed record SettleRentalDepositRequest(
    RentalRefundDestination Destination,
    string? IdempotencyKey = null);

/// <summary>
/// The admin surface of the clause 8 rental lifecycle: physical
/// delivery/return recording (the return anchors the seven-business-day
/// deposit window, Owner decision #20), the receipt-and-inspection record,
/// documented damage deductions (#21) and the deposit settlement whose award
/// is policy-derived — the caller chooses the destination and idempotency
/// key, never an amount.
/// <para>
/// Lifecycle operations use the refund-workflow policy (the clause 8 staff);
/// deduction approval is SuperAdmin-only (Owner decision #21), and the
/// settlement is money movement, so it rides the same policy as the other
/// refund executions.
/// </para>
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RefundWorkflowAdmin)]
[Route("api/v1/admin/rentals/bookings")]
public sealed class AdminRentalOperationsController(ISender sender, IDateTimeProvider clock) : ApiControllerBase
{
    [HttpGet("{rentalBookingId:guid}")]
    [ProducesResponseType(typeof(RentalBookingDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail(Guid rentalBookingId, CancellationToken cancellationToken)
    {
        return ToActionResult(await sender.Send(new GetRentalBookingAdminDetailQuery(rentalBookingId), cancellationToken));
    }

    [HttpPost("{rentalBookingId:guid}/delivery")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordDelivery(Guid rentalBookingId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new RecordRentalDeliveryCommand(rentalBookingId, actor.Value, clock.UtcNow),
            cancellationToken));
    }

    [HttpPost("{rentalBookingId:guid}/return")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordReturn(Guid rentalBookingId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new RecordRentalReturnCommand(rentalBookingId, actor.Value, clock.UtcNow),
            cancellationToken));
    }

    [HttpPost("{rentalBookingId:guid}/inspection")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CompleteInspection(Guid rentalBookingId, CompleteRentalInspectionRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new CompleteRentalInspectionCommand(rentalBookingId, actor.Value, request.Notes, clock.UtcNow),
            cancellationToken));
    }

    [HttpPost("{rentalBookingId:guid}/deductions")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordDeduction(Guid rentalBookingId, RecordRentalDamageDeductionRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new RecordRentalDamageDeductionCommand(
                rentalBookingId,
                request.DescriptionEnglish,
                request.DescriptionArabic,
                request.AmountMinorUnits,
                request.EvidenceReference,
                actor.Value,
                clock.UtcNow),
            cancellationToken));
    }

    /// <summary>Owner decision #21: a documented deduction is approved by a SuperAdmin, at most once.</summary>
    [HttpPost("deductions/{deductionId:guid}/approval")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveDeduction(Guid deductionId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new ApproveRentalDamageDeductionCommand(deductionId, actor.Value, clock.UtcNow),
            cancellationToken));
    }

    [HttpPost("deductions/{deductionId:guid}/dispute-resolution")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResolveDispute(Guid deductionId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new ResolveRentalDamageDisputeCommand(deductionId, actor.Value, clock.UtcNow),
            cancellationToken));
    }

    [HttpPost("{rentalBookingId:guid}/settle-deposit")]
    [ProducesResponseType(typeof(RentalBookingRefundResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SettleDeposit(Guid rentalBookingId, SettleRentalDepositRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new SettleRentalDepositCommand(rentalBookingId, actor.Value, request.Destination, request.IdempotencyKey, clock.UtcNow),
            cancellationToken));
    }
}
