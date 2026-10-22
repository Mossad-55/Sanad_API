using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Bookings;

namespace Sanad.API.Controllers;

public sealed record RecordCompletedCareHomeRefundRequest(string Reference, string Reason);

[Authorize(Policy = AuthorizationPolicies.CareHomesOperationalAdmin)]
[Route("api/v1/admin/care-homes/bookings/{bookingId:guid}/refund")]
public sealed class AdminCareHomeRefundsController(ISender sender, IDateTimeProvider clock) : ApiControllerBase
{
    [HttpPost("retry")]
    [ProducesResponseType(typeof(CareHomeBookingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(Guid bookingId, CancellationToken ct) =>
        TryGetAuthenticatedUserId(out UserId actor)
            ? ToActionResult(await sender.Send(new RetryCareHomeRefundCommand(actor, bookingId, clock.UtcNow), ct))
            : Unauthorized();

    [HttpPost("record-completed")]
    [ProducesResponseType(typeof(CareHomeBookingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordCompleted(
        Guid bookingId,
        RecordCompletedCareHomeRefundRequest request,
        CancellationToken ct) =>
        TryGetAuthenticatedUserId(out UserId actor)
            ? ToActionResult(await sender.Send(new RecordCareHomeRefundCompletedCommand(
                actor, bookingId, request.Reference, request.Reason, clock.UtcNow), ct))
            : Unauthorized();

    /// <summary>
    /// Operator reconciliation of an unconfirmed refund attempt (Owner decision
    /// #17): clears the booking's refund-ambiguity marker using only what the
    /// operator verified. <c>refundDidMove</c> must state a verified fact, not
    /// a guess: a verified movement completes the refund for exactly the
    /// recorded award, once; a verified non-payment returns the booking to the
    /// ordinary failed-refund state the retry route handles. Evidence must
    /// accompany the call, and <c>expectedAttemptClaimId</c> must carry the
    /// attempt identity (<c>refundAttemptClaimId</c> on the booking response)
    /// the operator read before verifying: a mismatch means the attempt has
    /// moved on and the request is refused with a stable conflict. A claimed
    /// attempt without a recorded provider outcome is never reconcilable from
    /// this surface (<c>refundReconciliationAttemptInconclusive</c>) — its
    /// recovery awaits the queued provider attempt-liveness seam.
    /// </summary>
    [HttpPost("reconcile")]
    [ProducesResponseType(typeof(CareHomeBookingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reconcile(
        Guid bookingId,
        ReconcileCareHomeRefundRequest request,
        CancellationToken ct) =>
        // Authentication is enforced exactly as on every other route; the
        // attempt-id shape is validated inside the handler as a stable 400
        // (CareHomes.RefundReconciliation.InvalidAttemptClaim) so an absent
        // id can never masquerade as an authorization failure.
        TryGetAuthenticatedUserId(out UserId actor)
            ? ToActionResult(await sender.Send(new ReconcileCareHomeRefundCommand(
                actor, bookingId, request.ExpectedAttemptClaimId, request.RefundDidMove,
                request.VerifiedProviderReference, request.Evidence, clock.UtcNow), ct))
            : Unauthorized();
}

// The attempt id is the compare-and-swap key and has no safe default; an
// absent (empty) id is rejected as a stable 400 validation failure inside
// the handler (authentication stays a separate, unchanged check).
public sealed record ReconcileCareHomeRefundRequest(
    Guid ExpectedAttemptClaimId, bool RefundDidMove, string? VerifiedProviderReference, string Evidence);
