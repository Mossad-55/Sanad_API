using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Finance.Application;

namespace Sanad.API.Controllers;

/// <summary>
/// Operations recovery for a rental refund case whose provider outcome was
/// never confirmed (Owner decisions #17 and #35). Every amount on this surface
/// is an <em>assertion</em> about a movement operations verified at the
/// provider, never an instruction: the Finance seam validates it against the
/// recorded approval and refuses a mismatch. The timeout and reconciliation
/// commands carry no amount at all; the timeout command derives it from the
/// case's standing approval.
/// </summary>
public sealed record RecordRentalRefundTimeoutRequest(string? ProviderSignal = null);

public sealed record ReconcileRentalRefundTimeoutRequest(
    string EvidenceReference,
    string? ProviderReference = null);

public sealed record CompleteRentalRefundRequest(
    long AmountMinorUnits,
    string ProviderReference,
    string EvidenceReference);

public sealed record ReverseRentalRefundRequest(
    long AmountMinorUnits,
    string? EvidenceReference = null);

/// <summary>
/// The admin recovery surface for rental refund cases. This is deliberately
/// SuperAdmin-only: these transitions record money that moved (or did not move)
/// outside the automatic path, and the seam cannot authenticate the actor.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.SuperAdminOnly)]
[Route("api/v1/admin/rentals/refund-cases")]
public sealed class AdminRentalRefundCasesController(ISender sender, IDateTimeProvider clock) : ApiControllerBase
{
    /// <summary>
    /// Records a durable timeout so the case blocks until reconciled. The amount
    /// is derived from the case's approval; a caller cannot name one.
    /// </summary>
    [HttpPost("{refundCaseId:guid}/timeout")]
    [ProducesResponseType(typeof(RentalRefundMovementSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordTimeout(
        Guid refundCaseId,
        RecordRentalRefundTimeoutRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new RecordRentalRefundTimeoutCommand(
                refundCaseId,
                actor.Value,
                clock.UtcNow,
                request.ProviderSignal),
            cancellationToken));
    }

    /// <summary>Records the operator's authenticated evidence for a timed-out case. Moves no money.</summary>
    [HttpPost("{refundCaseId:guid}/reconciliation")]
    [ProducesResponseType(typeof(RentalRefundMovementSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReconcileTimeout(
        Guid refundCaseId,
        ReconcileRentalRefundTimeoutRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new ReconcileRentalRefundTimeoutCommand(
                refundCaseId,
                actor.Value,
                request.EvidenceReference,
                clock.UtcNow,
                request.ProviderReference),
            cancellationToken));
    }

    /// <summary>
    /// Accounts a verified successful transfer. <c>amountMinorUnits</c> must
    /// equal the recorded award; <c>providerReference</c> and
    /// <c>evidenceReference</c> are mandatory.
    /// </summary>
    [HttpPost("{refundCaseId:guid}/completion")]
    [ProducesResponseType(typeof(RentalRefundMovementSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(
        Guid refundCaseId,
        CompleteRentalRefundRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new CompleteRentalRefundCommand(
                refundCaseId,
                request.AmountMinorUnits,
                request.ProviderReference,
                actor.Value,
                clock.UtcNow,
                request.EvidenceReference),
            cancellationToken));
    }

    /// <summary>
    /// Accounts verified non-payment so the case nets to zero and a fresh
    /// attempt can be made. <c>amountMinorUnits</c> must equal the standing
    /// award.
    /// </summary>
    [HttpPost("{refundCaseId:guid}/reversal")]
    [ProducesResponseType(typeof(RentalRefundMovementSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reverse(
        Guid refundCaseId,
        ReverseRentalRefundRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new ReverseRentalRefundCommand(
                refundCaseId,
                request.AmountMinorUnits,
                actor.Value,
                clock.UtcNow,
                request.EvidenceReference),
            cancellationToken));
    }
}
