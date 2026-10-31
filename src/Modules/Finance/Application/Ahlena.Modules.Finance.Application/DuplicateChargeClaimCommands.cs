using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Finance.Domain;

namespace Ahlena.Modules.Finance.Application;

/// <summary>
/// Stable error codes for the duplicate-charge support-approval workflow. The
/// API maps each code to its documented status; the application layer keeps the
/// reasons stable so a family read and an admin read never leak a raw
/// exception.
/// </summary>
public static class DuplicateChargeClaimErrors
{
    public static readonly Error NotFound =
        new("Finance.DuplicateClaim.NotFound", "The duplicate-charge claim was not found.");

    public static readonly Error Invalid =
        new("Finance.DuplicateClaim.Invalid", "The duplicate-charge claim input is not valid.");

    /// <summary>The claim is in a state that refuses the requested transition (verdict, execution or reconciliation).</summary>
    public static readonly Error InvalidState =
        new("Finance.DuplicateClaim.InvalidState", "The duplicate-charge claim is not in a state that allows this action.");

    /// <summary>A movement already exists under this claim's refund case, so a second award can never be recorded.</summary>
    public static readonly Error AlreadyAwarded =
        new("Finance.DuplicateClaim.AlreadyAwarded", "This claim's refund case has already recorded an award.");

    /// <summary>The capture budget cannot fund the FULL verified basis, so approval is refused, never trimmed.</summary>
    public static readonly Error CaptureBasisUnavailable =
        new("Finance.DuplicateClaim.CaptureBasisUnavailable", "The capture no longer has enough refundable funds for the full verified basis; resolve the conflict manually.");

    /// <summary>Another live claim (or legacy award) already consumed this capture for this family.</summary>
    public static readonly Error CaptureAlreadyClaimed =
        new("Finance.DuplicateClaim.CaptureAlreadyClaimed", "This capture is already claimed and cannot carry another duplicate-charge claim.");

    public static readonly Error IdempotencyConflict =
        new("Finance.DuplicateClaim.IdempotencyConflict", "This idempotency key was already used with different content.");

    /// <summary>
    /// The support decision surface is SuperAdmin+SupportAdmin (endpoint and
    /// seam); any other account type attempting a verdict is refused here even
    /// if the HTTP policy were ever widened by accident.
    /// </summary>
    public static readonly Error ReviewerRequired =
        new("Finance.DuplicateClaim.ReviewerRequired", "Only a SuperAdmin or a SupportAdmin may approve or reject a duplicate-charge claim.");

    /// <summary>Execution and reconciliation retain the restrictive money authority: SuperAdmin-only.</summary>
    public static readonly Error ExecutorRequired =
        new("Finance.DuplicateClaim.ExecutorRequired", "Only a SuperAdmin may execute or reconcile a duplicate-charge claim.");

    public static readonly Error EvidenceRequired =
        new("Finance.DuplicateClaim.EvidenceRequired", "A reconciliation must record the evidence the operator used.");

    public static readonly Error ProviderReferenceRequired =
        new("Finance.DuplicateClaim.ProviderReferenceRequired", "A reconciliation that records money as refunded must include the provider's refund reference.");

    /// <summary>The Owner-ordered reconciliation rule: NotMoved is never inferred from absence — affirmative provider evidence is mandatory.</summary>
    public static readonly Error NotMovedEvidenceRequired =
        new("Finance.DuplicateClaim.NotMovedEvidenceRequired", "A reconciliation that records money as NOT moved requires the affirmative operator-verified provider evidence reference.");

    /// <summary>The attempt this decision was prepared for is not the claim's current attempt (fencing token mismatch).</summary>
    public static readonly Error ExecutionAttemptMismatch =
        new("Finance.DuplicateClaim.ExecutionAttemptMismatch", "The claim's current execution attempt is not the attempt this decision was prepared for; refresh the claim and retry.");

    public static readonly Error AlreadySettled =
        new("Finance.DuplicateClaim.AlreadySettled", "This claim's money has already been recorded as settled.");

    /// <summary>An execution attempt is already in flight; execute is refused until its outcome lands or is reconciled.</summary>
    public static readonly Error NotExecutable =
        new("Finance.DuplicateClaim.NotExecutable", "This claim already has an execution attempt in progress or blocked for reconciliation.");

    /// <summary>The bounded execution-attempt budget is spent; further attempts require review.</summary>
    public static readonly Error ExecutableAttemptsExhausted =
        new("Finance.DuplicateClaim.ExecutableAttemptsExhausted", "This claim has used all of its execution attempts.");

    /// <summary>The capture has no resolvable provider transaction, so execution fails closed before any intent is staged.</summary>
    public static readonly Error ProviderTransactionMissing =
        new("Finance.DuplicateClaim.ProviderTransactionMissing", "The captured charge has no resolvable provider transaction; execution is refused.");

    /// <summary>An in-flight attempt cannot be reconciled as NotMoved: it may still be moving money at the provider.</summary>
    public static readonly Error IntentInFlight =
        new("Finance.DuplicateClaim.IntentInFlight", "The execution attempt is still in flight; without a verified provider reference it cannot be cleared, because the provider call may still move the money.");

    /// <summary>The unconfirmed attempt did not record an award, so a completed movement cannot be booked.</summary>
    public static readonly Error AwardUnknown =
        new("Finance.DuplicateClaim.AwardUnknown", "The unconfirmed attempt did not record an award, so a completed movement cannot be booked.");

    public static readonly Error Conflict =
        new("Finance.DuplicateClaim.Conflict", "The duplicate-charge claim was changed concurrently; retry.");

    public static readonly Error Unknown =
        new("Finance.DuplicateClaim.Unknown", "The duplicate-charge claim operation could not be completed.");
}

/// <summary>
/// Derivation of the refund case id shared with the frozen subscription-refund
/// machinery: byte 0 of the payment attempt id XORed with <c>0xD0</c> (the same
/// domain-separated derivation the legacy direct-award path uses). One corrected
/// charge therefore has exactly one refund case across submission, support
/// decision, claim execution and any historical legacy award, so the capture
/// budget and the case-ledger guard serialize them all.
/// <para>
/// Keep this byte-identical with the Families-side copy; changing either alone
/// would fork the money case of one charge.
/// </para>
/// </summary>
public static class SubscriptionRefundCaseIds
{
    public static Guid ForDuplicateCharge(Guid paymentAttemptId)
    {
        Span<byte> source = stackalloc byte[16];
        paymentAttemptId.TryWriteBytes(source);
        source[0] ^= 0xD0;
        return new Guid(source);
    }
}

/// <summary>
/// Seam-level role split for the duplicate-charge workflow. The HTTP policy
/// gates the surface to SuperAdmin+SupportAdmin; decisions are exercised with
/// the reviewer role; execution and reconciliation remain SuperAdmin-only
/// because they move or book actual gateway money. The actor's account type is
/// carried on the commands the same way the reviewed-complaint workflow carries
/// <c>ActorAccountType</c> — never re-derived from a token in the application
/// layer.
/// </summary>
public static class DuplicateChargeClaimRoleGuard
{
    public const string SuperAdminAccountType = "SuperAdmin";
    public const string SupportAdminAccountType = "SupportAdmin";

    public static bool IsReviewer(string? actorAccountType) =>
        string.Equals(actorAccountType?.Trim(), SuperAdminAccountType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(actorAccountType?.Trim(), SupportAdminAccountType, StringComparison.OrdinalIgnoreCase);

    public static bool IsExecutor(string? actorAccountType) =>
        string.Equals(actorAccountType?.Trim(), SuperAdminAccountType, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A duplicate-charge claim exactly as a FAMILY read may see it. This record is
/// the security boundary for family routes: it carries no verified basis, no
/// internal decision note, no evidence references, no decider/executor ids, no
/// execution attempt ids and no provider refund references — only the coarse
/// claim state, the coarse payout status, the mandatory customer-facing reason
/// when there is one, and the timestamps the family produced or needs.
/// </summary>
public sealed record FamilyDuplicateChargeClaimView(
    Guid ClaimId,
    Guid RefundCaseId,
    Guid PaymentAttemptId,
    Guid SubscriptionId,
    long EligibleMinorUnits,
    string Currency,
    DuplicateChargeClaimState ClaimState,
    string PayoutStatus,
    string? FamilyReason,
    DateTime SubmittedOnUtc,
    DateTime? DecidedOnUtc,
    bool IsReplay);

/// <summary>The full administrative view: every internal field, admin routes only.</summary>
public sealed record DuplicateChargeClaimAdminView(
    Guid ClaimId,
    Guid RefundCaseId,
    Guid FamilyAccountId,
    Guid SubscriptionId,
    Guid PaymentAttemptId,
    long AwardMinorUnits,
    string Currency,
    DuplicateChargeClaimState State,
    DuplicateChargeExecutionState ExecutionState,
    string? IdempotencyKey,
    string? EvidenceReference,
    DateTime CustomerReportedOnUtc,
    DateTime SubmittedOnUtc,
    Guid? DecidedByAccountId,
    string? DecidedByAccountType,
    DateTime? DecidedOnUtc,
    string? FamilyReason,
    string? DecisionNote,
    string? VerifiedBasis,
    Guid? ExecutionAttemptId,
    Guid? ExecutedByAccountId,
    DateTime? ExecutionStartedOnUtc,
    long? ExecutionAwardMinorUnits,
    string? GatewayErrorCode,
    string? ProviderRefundId,
    string? NotMovedProviderEvidenceReference,
    DateTime? CompletedOnUtc,
    int ExecutionAttemptCount,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

public static class DuplicateChargeClaimViews
{
    /// <summary>
    /// The coarse family-facing payout status. Deliberately unhurried: staff
    /// internals (ambiguous-outcome hygiene, attempt counts, error codes) never
    /// surface here; the family reads progress, not provider forensics.
    /// </summary>
    public static string PayoutStatusFor(DuplicateChargeClaimState state, DuplicateChargeExecutionState executionState) =>
        state switch
        {
            DuplicateChargeClaimState.Submitted => "pending",
            DuplicateChargeClaimState.Rejected => "rejected",
            _ => executionState switch
            {
                DuplicateChargeExecutionState.AwaitingExecution or DuplicateChargeExecutionState.None => "approved",
                DuplicateChargeExecutionState.Executing or DuplicateChargeExecutionState.Ambiguous => "processing",
                DuplicateChargeExecutionState.Failed => "failed",
                DuplicateChargeExecutionState.Completed => "completed",
                _ => "processing"
            }
        };
}

/// <summary>
/// The Finance-side persistence boundary for duplicate-charge claims —
/// submission, the support verdicts with their atomic reservation, and the
/// claim-before-send execution with its reconciliation matrix. Implemented in
/// Finance.Infrastructure on the Finance schema; no other module writes this
/// table.
/// </summary>
public interface IDuplicateChargeClaimStore
{
    Task<Result<FamilyDuplicateChargeClaimView>> SubmitAsync(
        DuplicateChargeClaimSubmissionInput input,
        CancellationToken cancellationToken);

    Task<Result<DuplicateChargeClaimAdminView>> ApproveAsync(
        Guid claimId,
        Guid decidedByAccountId,
        string? decidedByAccountType,
        DateTime decidedOnUtc,
        string verifiedBasis,
        string? decisionNote,
        string? familyReason,
        CancellationToken cancellationToken);

    Task<Result<DuplicateChargeClaimAdminView>> RejectAsync(
        Guid claimId,
        Guid decidedByAccountId,
        string? decidedByAccountType,
        DateTime decidedOnUtc,
        string familyReason,
        string? decisionNote,
        CancellationToken cancellationToken);

    Task<Result<DuplicateChargeClaimAdminView>> ExecuteAsync(
        Guid claimId,
        Guid? expectedExecutionAttemptId,
        Guid executedByAccountId,
        string? executedByAccountType,
        DateTime startedOnUtc,
        CancellationToken cancellationToken);

    Task<Result<DuplicateChargeClaimAdminView>> ReconcileAsync(
        DuplicateChargeClaimReconciliationInput input,
        CancellationToken cancellationToken);
}

/// <summary>
/// Everything Finance needs at submission, derived server-side by the purchase
/// owner (Families): the claim's facts, never a caller-supplied amount.
/// </summary>
public sealed record DuplicateChargeClaimSubmissionInput(
    Guid FamilyAccountId,
    Guid SubscriptionId,
    Guid PaymentAttemptId,
    Guid RefundCaseId,
    long BasisMinorUnits,
    string? IdempotencyKey,
    string? EvidenceReference,
    DateTime CustomerReportedOnUtc,
    DateTime SubmittedOnUtc);

/// <summary>
/// A reconciliation decision, bound to the execution attempt it was prepared
/// for. For an Ambiguous outcome the operator states what they found and this
/// time carries <em>affirmative</em>, operator-verified provider evidence in the
/// direction they assert: the provider's refund reference when the refund
/// moved, and an affirmative provider evidence reference (a statement or case id
/// from the provider surface) when they record it as NOT moved — mere
/// point-in-time absence of movement never settles an ambiguous attempt.
/// </summary>
public sealed record DuplicateChargeClaimReconciliationInput(
    Guid ClaimId,
    Guid? ExpectedExecutionAttemptId,
    bool RefundDidMove,
    string? VerifiedProviderRefundId,
    string? MovedEvidenceReference,
    string? NotMovedProviderEvidenceReference,
    string Evidence,
    Guid ReconciledByAccountId,
    string? ReconciledByAccountType,
    DateTime ReconciledOnUtc);

public interface IDuplicateChargeClaimReader
{
    Task<FamilyDuplicateChargeClaimView?> GetForFamilyAsync(
        Guid familyAccountId,
        Guid claimId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FamilyDuplicateChargeClaimView>> ListForFamilyAsync(
        Guid familyAccountId,
        CancellationToken cancellationToken);

    Task<DuplicateChargeClaimAdminView?> GetAdminAsync(
        Guid claimId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DuplicateChargeClaimAdminView>> ListAdminAsync(
        DuplicateChargeClaimState? state,
        DuplicateChargeExecutionState? executionState,
        Guid? familyAccountId,
        CancellationToken cancellationToken);
}

public sealed record ApproveDuplicateChargeClaimCommand(
    Guid ClaimId,
    Guid DecidedByAccountId,
    string? DecidedByAccountType,
    DateTime DecidedOnUtc,
    string VerifiedBasis,
    string? DecisionNote = null,
    string? FamilyReason = null) : ICommand<DuplicateChargeClaimAdminView>;

public sealed record RejectDuplicateChargeClaimCommand(
    Guid ClaimId,
    Guid DecidedByAccountId,
    string? DecidedByAccountType,
    DateTime DecidedOnUtc,
    string FamilyReason,
    string? DecisionNote = null) : ICommand<DuplicateChargeClaimAdminView>;

public sealed record ExecuteDuplicateChargeClaimCommand(
    Guid ClaimId,
    Guid? ExpectedExecutionAttemptId,
    Guid ExecutedByAccountId,
    string? ExecutedByAccountType,
    DateTime StartedOnUtc) : ICommand<DuplicateChargeClaimAdminView>;

public sealed record ReconcileDuplicateChargeClaimCommand(
    Guid ClaimId,
    Guid? ExpectedExecutionAttemptId,
    bool RefundDidMove,
    string? VerifiedProviderRefundId,
    string? MovedEvidenceReference,
    string? NotMovedProviderEvidenceReference,
    string Evidence,
    Guid ReconciledByAccountId,
    string? ReconciledByAccountType,
    DateTime ReconciledOnUtc) : ICommand<DuplicateChargeClaimAdminView>;

public sealed record GetDuplicateChargeClaimAdminQuery(Guid ClaimId) : IQuery<DuplicateChargeClaimAdminView?>;

public sealed record ListDuplicateChargeClaimsAdminQuery(
    DuplicateChargeClaimState? State = null,
    DuplicateChargeExecutionState? ExecutionState = null,
    Guid? FamilyAccountId = null) : IQuery<IReadOnlyList<DuplicateChargeClaimAdminView>>;

public sealed class ApproveDuplicateChargeClaimCommandHandler(IDuplicateChargeClaimStore store)
    : ICommandHandler<ApproveDuplicateChargeClaimCommand, DuplicateChargeClaimAdminView>
{
    public async Task<Result<DuplicateChargeClaimAdminView>> Handle(
        ApproveDuplicateChargeClaimCommand request,
        CancellationToken cancellationToken)
    {
        if (!DuplicateChargeClaimRoleGuard.IsReviewer(request.DecidedByAccountType))
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.ReviewerRequired);

        return await store.ApproveAsync(
            request.ClaimId,
            request.DecidedByAccountId,
            request.DecidedByAccountType,
            request.DecidedOnUtc,
            request.VerifiedBasis,
            request.DecisionNote,
            request.FamilyReason,
            cancellationToken);
    }
}

public sealed class RejectDuplicateClaimCommandHandler(IDuplicateChargeClaimStore store)
    : ICommandHandler<RejectDuplicateChargeClaimCommand, DuplicateChargeClaimAdminView>
{
    public async Task<Result<DuplicateChargeClaimAdminView>> Handle(
        RejectDuplicateChargeClaimCommand request,
        CancellationToken cancellationToken)
    {
        if (!DuplicateChargeClaimRoleGuard.IsReviewer(request.DecidedByAccountType))
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.ReviewerRequired);

        if (string.IsNullOrWhiteSpace(request.FamilyReason))
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Invalid);

        return await store.RejectAsync(
            request.ClaimId,
            request.DecidedByAccountId,
            request.DecidedByAccountType,
            request.DecidedOnUtc,
            request.FamilyReason,
            request.DecisionNote,
            cancellationToken);
    }
}

public sealed class ExecuteDuplicateChargeClaimCommandHandler(IDuplicateChargeClaimStore store)
    : ICommandHandler<ExecuteDuplicateChargeClaimCommand, DuplicateChargeClaimAdminView>
{
    public async Task<Result<DuplicateChargeClaimAdminView>> Handle(
        ExecuteDuplicateChargeClaimCommand request,
        CancellationToken cancellationToken)
    {
        if (!DuplicateChargeClaimRoleGuard.IsExecutor(request.ExecutedByAccountType))
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.ExecutorRequired);

        return await store.ExecuteAsync(
            request.ClaimId,
            request.ExpectedExecutionAttemptId,
            request.ExecutedByAccountId,
            request.ExecutedByAccountType,
            request.StartedOnUtc,
            cancellationToken);
    }
}

public sealed class ReconcileDuplicateChargeClaimCommandHandler(IDuplicateChargeClaimStore store)
    : ICommandHandler<ReconcileDuplicateChargeClaimCommand, DuplicateChargeClaimAdminView>
{
    public async Task<Result<DuplicateChargeClaimAdminView>> Handle(
        ReconcileDuplicateChargeClaimCommand request,
        CancellationToken cancellationToken)
    {
        if (!DuplicateChargeClaimRoleGuard.IsExecutor(request.ReconciledByAccountType))
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.ExecutorRequired);

        if (string.IsNullOrWhiteSpace(request.Evidence))
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.EvidenceRequired);

        // Money may only be booked as moved against the provider's own reference:
        // an operator note alone is not evidence that a refund left the gateway.
        if (request.RefundDidMove && string.IsNullOrWhiteSpace(request.VerifiedProviderRefundId))
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.ProviderReferenceRequired);

        // The Owner-ordered rule for ambiguous attempts: "did not move" is never
        // inferred from absence; it requires the affirmative, operator-verified
        // provider evidence reference.
        if (!request.RefundDidMove && string.IsNullOrWhiteSpace(request.NotMovedProviderEvidenceReference))
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.NotMovedEvidenceRequired);

        return await store.ReconcileAsync(
            new DuplicateChargeClaimReconciliationInput(
                request.ClaimId,
                request.ExpectedExecutionAttemptId,
                request.RefundDidMove,
                request.VerifiedProviderRefundId,
                request.MovedEvidenceReference,
                request.NotMovedProviderEvidenceReference,
                request.Evidence,
                request.ReconciledByAccountId,
                request.ReconciledByAccountType,
                request.ReconciledOnUtc),
            cancellationToken);
    }
}

public sealed class GetDuplicateChargeClaimAdminQueryHandler(IDuplicateChargeClaimReader reader)
    : IQueryHandler<GetDuplicateChargeClaimAdminQuery, DuplicateChargeClaimAdminView?>
{
    public async Task<Result<DuplicateChargeClaimAdminView?>> Handle(
        GetDuplicateChargeClaimAdminQuery request,
        CancellationToken cancellationToken) =>
        Result<DuplicateChargeClaimAdminView?>.Success(
            await reader.GetAdminAsync(request.ClaimId, cancellationToken));
}

public sealed class ListDuplicateChargeClaimsAdminQueryHandler(IDuplicateChargeClaimReader reader)
    : IQueryHandler<ListDuplicateChargeClaimsAdminQuery, IReadOnlyList<DuplicateChargeClaimAdminView>>
{
    public async Task<Result<IReadOnlyList<DuplicateChargeClaimAdminView>>> Handle(
        ListDuplicateChargeClaimsAdminQuery request,
        CancellationToken cancellationToken) =>
        Result<IReadOnlyList<DuplicateChargeClaimAdminView>>.Success(
            await reader.ListAdminAsync(request.State, request.ExecutionState, request.FamilyAccountId, cancellationToken));
}
