using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Identity.Application.Authentication.Tokens;

namespace Sanad.API.Controllers;

// Audit instants (support decision, execution, reconciliation, completion) are
// SERVER-AUTHORITATIVE under the accepted R2 design: this controller stamps
// them from the injected IDateTimeProvider.UtcNow at action time. Request
// bodies deliberately carry no timestamps, so a client can never author,
// falsify or replay audit time on the money surfaces.

public sealed record ApproveDuplicateChargeClaimRequest(
    string VerifiedBasis,
    string? DecisionNote = null,
    string? FamilyReason = null);

public sealed record RejectDuplicateChargeClaimRequest(
    string FamilyReason,
    string? DecisionNote = null);

public sealed record ExecuteDuplicateChargeClaimRequest(Guid? ExpectedExecutionAttemptId = null);

public sealed record ReconcileDuplicateChargeClaimRequest(
    bool RefundDidMove,
    string Evidence,
    // Mandatory affirmative provider evidence per direction: the provider's
    // own refund reference when the refund moved; an operator-verified
    // provider evidence reference when it did NOT move. Absence is never
    // evidence of absence (Owner-ordered rule).
    string? VerifiedProviderRefundId = null,
    string? MovedEvidenceReference = null,
    string? NotMovedProviderEvidenceReference = null,
    Guid? ExpectedExecutionAttemptId = null);

/// <summary>
/// SuperAdmin/SupportAdmin surface for duplicate-charge support claims
/// (Owner-accepted design). List/detail/approve/reject are refund-workflow
/// review actions under <see cref="AuthorizationPolicies.RefundWorkflowAdmin"/>;
/// execute/reconcile move or book actual gateway money and therefore carry the
/// nested <see cref="AuthorizationPolicies.SuperAdminOnly"/> policy AND a
/// SuperAdmin-only seam guard, so the restrictive money authority holds even if
/// the endpoint policy were ever widened.
/// <para>
/// Approval claims the FULL verified capture basis atomically (verdict +
/// capture-budget reservation + ledger entry in one transaction); execution
/// persists its intent before the provider call; an unresolved provider
/// outcome blocks everything except reconciliation, and a NotMoved
/// reconciliation requires affirmative, operator-verified provider evidence.
/// All audit instants (decision, execution, reconciliation, completion) are
/// SERVER-AUTHORITATIVE under R2: stamped from the injected
/// <see cref="IDateTimeProvider.UtcNow"/> at action time; request bodies
/// deliberately carry no timestamps, so a client can never author audit time.
/// </para>
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RefundWorkflowAdmin)]
[Route("api/v1/admin/finance/subscription-refunds/duplicate-charge")]
public sealed class AdminFinanceDuplicateChargeClaimsController(ISender sender, IDateTimeProvider clock) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DuplicateChargeClaimAdminView>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] DuplicateChargeClaimState? state,
        [FromQuery] DuplicateChargeExecutionState? executionState,
        [FromQuery] Guid? familyId,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<DuplicateChargeClaimAdminView>> result = await sender.Send(
            new ListDuplicateChargeClaimsAdminQuery(state, executionState, familyId),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ClaimProblem(result.Error);
    }

    [HttpGet("{claimId:guid}")]
    [ProducesResponseType(typeof(DuplicateChargeClaimAdminView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Detail(
        Guid claimId,
        CancellationToken cancellationToken)
    {
        Result<DuplicateChargeClaimAdminView?> result = await sender.Send(
            new GetDuplicateChargeClaimAdminQuery(claimId),
            cancellationToken);
        return result.IsSuccess
            ? result.Value is null
                ? NotFoundProblem(DuplicateChargeClaimErrors.NotFound)
                : Ok(result.Value)
            : ClaimProblem(result.Error);
    }

    [HttpPost("{claimId:guid}/approve")]
    [ProducesResponseType(typeof(DuplicateChargeClaimAdminView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(
        Guid claimId,
        ApproveDuplicateChargeClaimRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var actorUserId))
            return Unauthorized();

        Result<DuplicateChargeClaimAdminView> result = await sender.Send(
            new ApproveDuplicateChargeClaimCommand(
                claimId,
                actorUserId.Value,
                CurrentAccountType,
                clock.UtcNow,
                request.VerifiedBasis,
                request.DecisionNote,
                request.FamilyReason),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ClaimProblem(result.Error);
    }

    [HttpPost("{claimId:guid}/reject")]
    [ProducesResponseType(typeof(DuplicateChargeClaimAdminView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(
        Guid claimId,
        RejectDuplicateChargeClaimRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var actorUserId))
            return Unauthorized();

        Result<DuplicateChargeClaimAdminView> result = await sender.Send(
            new RejectDuplicateChargeClaimCommand(
                claimId,
                actorUserId.Value,
                CurrentAccountType,
                clock.UtcNow,
                request.FamilyReason,
                request.DecisionNote),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ClaimProblem(result.Error);
    }

    /// <summary>
    /// Triggers one execution attempt: intent (new attempt id, committed award,
    /// TransferInitiated) is persisted BEFORE the provider call; the provider
    /// call maps to confirmed / refused / unconfirmed outcomes. SuperAdmin
    /// authority is enforced by both the nested policy and the seam guard.
    /// </summary>
    [HttpPost("{claimId:guid}/execute")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdminOnly)]
    [ProducesResponseType(typeof(DuplicateChargeClaimAdminView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Execute(
        Guid claimId,
        ExecuteDuplicateChargeClaimRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var actorUserId))
            return Unauthorized();

        Result<DuplicateChargeClaimAdminView> result = await sender.Send(
            new ExecuteDuplicateChargeClaimCommand(
                claimId,
                request.ExpectedExecutionAttemptId,
                actorUserId.Value,
                CurrentAccountType,
                clock.UtcNow),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ClaimProblem(result.Error);
    }

    /// <summary>
    /// Reconciles an in-flight or unconfirmed attempt, bound to the execution
    /// attempt it was prepared for. Moved requires the provider's own refund
    /// reference; NotMoved requires the affirmative operator-verified provider
    /// evidence reference — a point-in-time absence never settles an attempt.
    /// SuperAdmin-only (policy + seam).
    /// </summary>
    [HttpPost("{claimId:guid}/reconcile")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdminOnly)]
    [ProducesResponseType(typeof(DuplicateChargeClaimAdminView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reconcile(
        Guid claimId,
        ReconcileDuplicateChargeClaimRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var actorUserId))
            return Unauthorized();

        Result<DuplicateChargeClaimAdminView> result = await sender.Send(
            new ReconcileDuplicateChargeClaimCommand(
                claimId,
                request.ExpectedExecutionAttemptId,
                request.RefundDidMove,
                request.VerifiedProviderRefundId,
                request.MovedEvidenceReference,
                request.NotMovedProviderEvidenceReference,
                request.Evidence,
                actorUserId.Value,
                CurrentAccountType,
                clock.UtcNow),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ClaimProblem(result.Error);
    }

    /// <summary>
    /// The authenticated actor's account type claim, forwarded to the seam so
    /// the reviewer (SuperAdmin+SupportAdmin) and executor (SuperAdmin-only)
    /// role split is enforced inside the handlers too, never only by policy.
    /// </summary>
    private string? CurrentAccountType => User.FindFirst(AuthClaimNames.AccountType)?.Value;

    private IActionResult NotFoundProblem(Error error) =>
        ClaimProblem(error);

    private IActionResult ClaimProblem(Error error)
    {
        int status = error.Code switch
        {
            "Finance.DuplicateClaim.NotFound" => StatusCodes.Status404NotFound,
            "Finance.DuplicateClaim.Invalid" or
                "Finance.DuplicateClaim.EvidenceRequired" or
                "Finance.DuplicateClaim.ProviderReferenceRequired" or
                "Finance.DuplicateClaim.NotMovedEvidenceRequired" => StatusCodes.Status400BadRequest,
            "Finance.DuplicateClaim.ReviewerRequired" or
                "Finance.DuplicateClaim.ExecutorRequired" => StatusCodes.Status403Forbidden,
            // Everything else is a state or concurrency conflict with the
            // workflow (verdict already cast, capture consumed, in-flight
            // intent, attempt mismatch, exhausted retries) — a 409, matching
            // the documented decision surfaces.
            _ => StatusCodes.Status409Conflict
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = status switch
            {
                StatusCodes.Status404NotFound => "Not Found",
                StatusCodes.Status409Conflict => "Conflict",
                StatusCodes.Status403Forbidden => "Forbidden",
                _ => "Bad Request"
            },
            Detail = error.Message,
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(status, problem);
    }
}
