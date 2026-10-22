using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Rentals.Domain;
using FinanceRefundErrors = Sanad.Modules.Finance.Application.RentalRefundErrors;

namespace Sanad.Modules.Rentals.Application;

public static class RentalRefundErrors
{
    public static readonly Error DestinationUnavailable =
        new("Rentals.Refund.DestinationUnavailable", "This refund destination is not available in this stage.");

    public static readonly Error NoPaymentReference =
        new("Rentals.Refund.NoPaymentReference", "The captured booking has no stored gateway transaction to refund.");

    public static readonly Error OutcomePendingReconciliation =
        new("Rentals.Refund.OutcomePendingReconciliation", "The refund outcome is unknown and must be reconciled by operations before it can proceed.");

    public static readonly Error AttemptFailedRetryable =
        new("Rentals.Refund.AttemptFailedRetryable", "The previous refund attempt failed and was reversed; it may be retried.");

    public static readonly Error AlreadyRecorded =
        new("Rentals.Refund.AlreadyRecorded", "This refund was already recorded with a different content.");

    /// <summary>Stable 400 (explicitly mapped in the shared problem-details mapper).</summary>
    public static readonly Error IdempotencyKeyTooLong =
        new("Rentals.Refund.IdempotencyKeyTooLong", "The idempotency key is too long for the refund evidence budget.");

    /// <summary>Stable 400: a negative amount can never name a captured booking movement.</summary>
    public static readonly Error InvalidAmount =
        new("Rentals.Refund.InvalidAmount", "The refund amount is invalid.");
}

/// <summary>
/// Executes — or safely resumes — one rental refund movement through the Finance
/// seams. The crash-safe ordering (Owner decisions #16, #17, #4):
/// <list type="number">
/// <item>the Finance refund-case correlation id is claimed on the booking row and
/// committed with the booking's optimistic-concurrency token BEFORE any money
/// moves, so two concurrent refunds cannot both claim (the loser is a retryable
/// conflict, never a second movement);</item>
/// <item>the Finance ledger award is recorded (approval), then the payment rails
/// run; a definitive rails refusal is signed off (the only negative entry, netting
/// the case to zero) and the claim is released for a safe retry; an ambiguous
/// outcome is durably timed out and the claim stays pending — it can only be
/// resolved by operations reconciliation, never by an automatic resend;</item>
/// <item>any retry classifies the stored Finance case first: a recorded completion
/// (transfer confirmed or credit posted) is replayed onto the booking as its
/// movement row WITHOUT moving money again; a recorded reversal releases the
/// claim; an approval standing alone, a timeout, or a reconciled case awaiting
/// outcomes all block with a stable conflict until operations resolve them.</item>
/// </list>
/// <para>
/// Destination semantics (Owner decision #10): the original payment method is
/// refunded through the payment rails; app credit is atomically posted inside the
/// Finance persistence boundary — in ONE Finance unit of work the case records
/// the authoritative Approved entry AND the attributable CreditBalancePosted
/// entry under the refund case id, plus the balance movement, exactly once per
/// refund case (decisions #15, M1 closure; replay validates case + family +
/// amount + evidence). A zero award records the approval entry only — no rails
/// call, no credit post (#31: a zero award is a valid, attributable outcome,
/// complete at the approval).
/// </para>
/// <para>
/// The payload check on resume reuses the ledger's own content equality for the
/// one classification that is still waiting on the caller's movement (an
/// approval standing alone): the composed evidence embeds the movement kind, the
/// destination and the caller's idempotency key, so a retry with different
/// content is refused and the in-flight claim is never erased by different
/// content. Recorded outcomes replay their own stored content — including the
/// RECORDED destination, which the resume surfaces instead of any new request
/// (M3 destination binding); a verified reversal releases its dead claim
/// whatever the retry carries, because no money ever moved under it. Any
/// exception escaping the provider client is treated as an ambiguous outcome
/// (durable timeout, never a reversal, never a resend).
/// </para>
/// </summary>
/// <summary>Public so the unit-test assembly can drive movement classification directly; consumers should always go through the MediatR handlers.</summary>
public static class RentalRefundExecutor
{
    /// <summary>
    /// Composes the caller's evidence reference: booking, movement kind, destination
    /// and idempotency key. The Finance seam embeds this in its own case-level
    /// evidence, which makes the embedded destination/key part of the ledger's
    /// content match on the resume path.
    /// </summary>
    public static string ComposeEvidence(Guid rentalBookingId, RentalRefundMovementKind kind, RentalRefundDestination destination, string? idempotencyKey)
    {
        string evidence = $"rental-booking:{rentalBookingId};kind:{kind};dest:{destination}";
        string? key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey!.Trim();
        return key is null ? evidence : $"{evidence};key:{key}";
    }

    /// <summary>
    /// The longest caller idempotency key that provably fits the evidence budget,
    /// derived from the composition templates rather than a magic number. The
    /// caller's key appears TWICE in the tightest (rails) composition: once
    /// inline in the Rentals evidence and once in the Finance case-level
    /// composition — <c>reason:&lt;token&gt;;key:&lt;k&gt;;ref:&lt;rentals
    /// evidence incl. ;key:k&gt;</c> — which the frozen ledger caps at
    /// <see cref="RefundLedgerEntry.MaximumEvidenceReferenceLength"/> (300):
    /// <c>MaxKey = (300 − reason(42) − ";key:"(5) − ";ref:"(5) −
    /// base(111)) / 2 = 68</c>. Validated before any claim
    /// or Finance call, so an oversized key is a stable 400, never a late
    /// DomainException on a half-executed movement.
    /// </summary>
    public static readonly int MaximumIdempotencyKeyLength =
        (RefundLedgerEntry.MaximumEvidenceReferenceLength
            - "reason:rental-cancellation-before-delivery".Length // the longest Finance reason token
            - ";key:".Length - ";ref:".Length                     // the Finance composition's own overhead
            - ("rental-booking:".Length + 36                      // Guid 'D' textual length
                + ";kind:".Length + nameof(RentalRefundMovementKind.CancellationFullRefund).Length
                + ";dest:".Length + nameof(RentalRefundDestination.OriginalPaymentMethod).Length
                + ";key:".Length                                  // the inline key slot in our evidence
              )) / 2;

    public static async Task<RefundExecutionResult> ExecuteOrResumeAsync(
        RentalRefundReason reason,
        Guid refundCaseId,
        long claimedAmountMinorUnits,
        Guid familyId,
        Guid actorUserId,
        DateTime utcNow,
        string evidenceReference,
        string? originalGatewayTransactionId,
        string? idempotencyKey,
        RentalRefundDestination destination,
        IPaymobClient paymobClient,
        IRentalRefundRecorder recorder,
        CancellationToken cancellationToken)
    {
        if (claimedAmountMinorUnits < 0)
            return RefundExecutionResult.RetryableFailure(refundCaseId, destination, RentalRefundErrors.InvalidAmount);
        if (idempotencyKey is not null && idempotencyKey.Length > MaximumIdempotencyKeyLength)
            return RefundExecutionResult.RetryableFailure(refundCaseId, destination, RentalRefundErrors.IdempotencyKeyTooLong);

        // 1) Read the stored case: null means nothing was ever recorded under this
        //    claim — safe to execute fresh. Anything else is classified FIRST, and
        //    no path below re-executes money for an already recorded outcome.
        Result<RentalRefundCaseSnapshot?> read = await recorder.ReadCaseAsync(refundCaseId, cancellationToken);
        if (read.IsFailure)
            return RefundExecutionResult.KeepClaim(refundCaseId, destination, read.Error);

        if (read.Value is not { } existing)
        {
            return await ExecuteFreshAsync(
                reason, refundCaseId, claimedAmountMinorUnits, familyId, actorUserId, utcNow,
                evidenceReference, originalGatewayTransactionId, idempotencyKey, destination,
                paymobClient, recorder, cancellationToken);
        }

        switch (existing.LatestEntryType)
        {
            case null:
            case RefundLedgerEntryType.Requested:
            case RefundLedgerEntryType.TransferInitiated:
                // Nothing has moved yet (only informational entries, if any): execute
                // the movement — the approval content-gate runs inside, and a same-
                // content replay returns the recorded outcome instead of re-recording.
                return await ExecuteFreshAsync(
                    reason, refundCaseId, claimedAmountMinorUnits, familyId, actorUserId, utcNow,
                    evidenceReference, originalGatewayTransactionId, idempotencyKey, destination,
                    paymobClient, recorder, cancellationToken);

            case RefundLedgerEntryType.Rejected:
                return RefundExecutionResult.KeepClaim(refundCaseId, destination, RentalRefundErrors.AlreadyRecorded);

            case RefundLedgerEntryType.Approved when claimedAmountMinorUnits == 0:
                // A zero award is complete at the approval entry (#31) — replay it.
                return RefundExecutionResult.CommittedReplay(
                    refundCaseId, 0, ReplayDestination(existing, destination), gatewayReference: null);

            case RefundLedgerEntryType.Approved:
            {
                // The award stands recorded but the movement's outcome is not provable
                // from here: the process may have died before or after the provider
                // call. The payload gate (the ledger's own content equality via the
                // replaying approval call) must pass BEFORE anything else happens — a
                // retry with different content refuses and the claim is untouched.
                Result<RentalRefundMovementSnapshot> payloadGate = await recorder.RecordTransferAsync(
                    refundCaseId,
                    Money.FromMinorUnits(claimedAmountMinorUnits),
                    reason,
                    actorUserId,
                    utcNow,
                    idempotencyKey,
                    providerReference: null,
                    evidenceReference,
                    cancellationToken);
                if (payloadGate.IsFailure)
                    return payloadGate.Error.Code == FinanceRefundErrors.IdempotencyConflict.Code
                        ? RefundExecutionResult.KeepClaim(refundCaseId, destination, RentalRefundErrors.AlreadyRecorded)
                        : RefundExecutionResult.KeepClaim(refundCaseId, destination, payloadGate.Error);

                // Not provable ⇒ ambiguous (#17): record the durable timeout and stop.
                // Never a signed reversal, never an automatic resend.
                Result<RentalRefundMovementSnapshot> timedOut = await recorder.RecordTimeoutAsync(
                    refundCaseId, Money.FromMinorUnits(claimedAmountMinorUnits), actorUserId, utcNow,
                    providerSignal: "resume:approval-unconfirmed", cancellationToken);
                if (timedOut.IsFailure)
                    return RefundExecutionResult.KeepClaim(refundCaseId, destination, timedOut.Error);

                return RefundExecutionResult.Blocked(refundCaseId, destination, RentalRefundErrors.OutcomePendingReconciliation);
            }

            case RefundLedgerEntryType.TimedOut:
            case RefundLedgerEntryType.Failed when existing.LatestAmountMinorUnits == 0 && HasPrecedingTimeout(existing):
                // A durably recorded ambiguous outcome, or a reconciled case whose
                // outcome accounting is still pending: operations owns it (#17).
                // NOTE: a zero-amount Failed WITHOUT a preceding timeout is NOT a
                // reconciliation marker — it is a zero reversal (nothing moved),
                // and it must not block forever; the arm below releases it.
                return RefundExecutionResult.Blocked(refundCaseId, destination, RentalRefundErrors.OutcomePendingReconciliation);

            case RefundLedgerEntryType.Failed:
                // A signed reversal: verified nonpayment (or a zero reversal that
                // never moved anything). The claim releases and a retry starts a
                // fresh case. Content-independent: nothing ever moved under it.
                return RefundExecutionResult.RetryableFailure(refundCaseId, destination, RentalRefundErrors.AttemptFailedRetryable);

            case RefundLedgerEntryType.TransferConfirmed:
                // Crash window after the Finance commit and before the local save:
                // replay the stored outcome — never execute money again (M3: the
                // replays' destination is the recorded one, whatever this call
                // asked for).
                if (existing.LatestAmountMinorUnits != claimedAmountMinorUnits)
                    return RefundExecutionResult.KeepClaim(refundCaseId, destination, RentalRefundErrors.AlreadyRecorded);

                return RefundExecutionResult.CommittedReplay(
                    refundCaseId, claimedAmountMinorUnits, ReplayDestination(existing, destination), existing.LatestProviderReference);

            case RefundLedgerEntryType.CreditBalancePosted:
                // A completed credit case retried toward the rails replays the
                // stored CREDIT outcome — the recorded destination (AppCredit) wins
                // over the caller's re-request; no transfer is ever recorded.
                if (existing.LatestAmountMinorUnits != claimedAmountMinorUnits)
                    return RefundExecutionResult.KeepClaim(refundCaseId, destination, RentalRefundErrors.AlreadyRecorded);

                return RefundExecutionResult.CommittedReplay(
                    refundCaseId, claimedAmountMinorUnits, ReplayDestination(existing, destination), gatewayReference: null);

            default:
                return RefundExecutionResult.KeepClaim(refundCaseId, destination, RentalRefundErrors.OutcomePendingReconciliation);
        }
    }

    /// <summary>
    /// True when the latest zero-amount Failed entry is a RECONCILIATION MARKER —
    /// a zero reversal without a preceding timeout means "nothing moved and was
    /// reversed at zero"; it is a verified non-movement and must not block the
    /// case forever.
    /// </summary>
    private static bool HasPrecedingTimeout(RentalRefundCaseSnapshot existing) =>
        existing.Entries.Any(entry => entry.EntryType == RefundLedgerEntryType.TimedOut);

    /// <summary>
    /// Recovers the recorded movement destination from a case's evidence chain
    /// (the composed evidence embeds <c>dest:&lt;value&gt;</c>). Falls back to
    /// the caller's requested destination only when nothing is recoverable —
    /// a replay must always surface the persisted/parsed destination, never
    /// silently adopt a different re-request (M3 destination binding).
    /// </summary>
    private static RentalRefundDestination ReplayDestination(RentalRefundCaseSnapshot existing, RentalRefundDestination requested)
    {
        string? evidence = existing.Entries.Count > 0
            ? existing.Entries[^1].EvidenceReference
            : null;

        if (evidence is not null)
        {
            int marker = evidence.IndexOf(";dest:", StringComparison.Ordinal);
            if (marker >= 0)
            {
                int start = marker + ";dest:".Length;
                int end = evidence.IndexOf(';', start);
                string token = end < 0 ? evidence[start..] : evidence[start..end];
                if (Enum.TryParse(token, out RentalRefundDestination parsed) && Enum.IsDefined(parsed))
                    return parsed;
            }
        }

        return requested;
    }

    private static async Task<RefundExecutionResult> ExecuteFreshAsync(
        RentalRefundReason reason,
        Guid refundCaseId,
        long claimedAmountMinorUnits,
        Guid familyId,
        Guid actorUserId,
        DateTime utcNow,
        string evidenceReference,
        string? originalGatewayTransactionId,
        string? idempotencyKey,
        RentalRefundDestination destination,
        IPaymobClient paymobClient,
        IRentalRefundRecorder recorder,
        CancellationToken cancellationToken)
    {
        if (claimedAmountMinorUnits == 0)
        {
            // Zero award: an attributable approval entry, no money moves (#31).
            Result<RentalRefundMovementSnapshot> zeroAward = await recorder.RecordTransferAsync(
                refundCaseId,
                Money.FromMinorUnits(0),
                reason,
                actorUserId,
                utcNow,
                idempotencyKey,
                providerReference: null,
                evidenceReference,
                cancellationToken);
            if (zeroAward.IsFailure)
                return zeroAward.Error.Code == FinanceRefundErrors.IdempotencyConflict.Code
                    ? RefundExecutionResult.KeepClaim(refundCaseId, destination, RentalRefundErrors.AlreadyRecorded)
                    : RefundExecutionResult.KeepClaim(refundCaseId, destination, zeroAward.Error);

            return RefundExecutionResult.Committed(refundCaseId, 0, destination, gatewayReference: null, zeroAward.Value.IsReplay);
        }

        switch (destination)
        {
            case RentalRefundDestination.AppCredit:
            {
                // ONE atomic Finance unit of work: the authoritative Approved entry,
                // the attributable CreditBalancePosted entry and the balance movement
                // commit together, exactly once per refund case (decision #15, M1
                // closure); replay validates case + family + amount + evidence, and a
                // same-case retry reads the recorded credit instead of posting again.
                Result<RentalRefundMovementSnapshot> posted = await recorder.PostRentalRefundCreditAsync(
                    refundCaseId, familyId, Money.FromMinorUnits(claimedAmountMinorUnits),
                    actorUserId, utcNow, evidenceReference, cancellationToken);

                if (posted.IsFailure)
                {
                    // Conflict/Unknown/IdempotencyConflict mean the case may already
                    // stand somewhere else — the claim stays pending and the resume
                    // classification owns recovery (its retry replays instead of
                    // re-posting). Any other code is a definite local refusal with
                    // nothing committed: the claim releases for a safe retry.
                    bool keepClaimed =
                        posted.Error.Code == FinanceRefundErrors.Conflict.Code ||
                        posted.Error.Code == FinanceRefundErrors.Unknown.Code ||
                        posted.Error.Code == FinanceRefundErrors.IdempotencyConflict.Code;

                    return keepClaimed
                        ? RefundExecutionResult.KeepClaim(refundCaseId, destination, posted.Error)
                        : RefundExecutionResult.RetryableFailure(refundCaseId, destination, posted.Error);
                }

                return RefundExecutionResult.Committed(
                    refundCaseId, claimedAmountMinorUnits, destination, gatewayReference: null, posted.Value.IsReplay);
            }

            case RentalRefundDestination.OriginalPaymentMethod when originalGatewayTransactionId is not { Length: > 0 }:
                // Missing provider reference must be validated BEFORE approval/claim
                // where possible (the handler pre-validates; this is the last line).
                // Nothing was recorded yet, so releasing the fresh claim is safe.
                return RefundExecutionResult.RetryableFailure(refundCaseId, destination, RentalRefundErrors.NoPaymentReference);

            case RentalRefundDestination.OriginalPaymentMethod:
            {
                Money award = Money.FromMinorUnits(claimedAmountMinorUnits);
                string gatewayTransactionId = originalGatewayTransactionId!.Trim();

                // Approval first: the in-flight ceiling (#35 NetRecorded) starts
                // counting before the rails are touched.
                Result<RentalRefundMovementSnapshot> approval = await recorder.RecordTransferAsync(
                    refundCaseId, award, reason, actorUserId, utcNow, idempotencyKey,
                    providerReference: null, evidenceReference, cancellationToken);
                if (approval.IsFailure)
                    return approval.Error.Code == FinanceRefundErrors.IdempotencyConflict.Code
                        ? RefundExecutionResult.KeepClaim(refundCaseId, destination, RentalRefundErrors.AlreadyRecorded)
                        : RefundExecutionResult.KeepClaim(refundCaseId, destination, approval.Error);

                // The provider call is the one place the process can die with the
                // outcome unknown: ANY escaping exception — transport faults, a
                // host crash mid-call, cancellation — is normalised to the
                // ambiguous taxonomy and durably timed out below (#17: never a
                // reversal on an unconfirmed outcome, never a resend).
                Result<string?> refund;
                try
                {
                    refund = await paymobClient.RefundPaymentAsync(
                        gatewayTransactionId,
                        award.MajorUnits,
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    refund = Result<string?>.Failure(new Error(
                        "Paymob.RefundOutcomeUnknown",
                        $"The refund call did not return an outcome ({exception.GetType().Name})."));
                }

                if (refund.IsFailure && IsOutcomeUnknown(refund.Error))
                {
                    // 4xx refusals stay definitive; everything that could not
                    // confirm the outcome records the durable fence — WITH a
                    // non-cancellable token, because the unknown-outcome record
                    // is the safety property the next resume must find even when
                    // the caller is gone.
                    Result<RentalRefundMovementSnapshot> timedOut = await recorder.RecordTimeoutAsync(
                        refundCaseId, award, actorUserId, utcNow,
                        providerSignal: refund.Error?.Code ?? "Paymob.RefundOutcomeUnknown",
                        CancellationToken.None);
                    if (timedOut.IsFailure)
                        return RefundExecutionResult.KeepClaim(refundCaseId, destination, timedOut.Error);

                    return RefundExecutionResult.Blocked(refundCaseId, destination, RentalRefundErrors.OutcomePendingReconciliation);
                }

                if (refund.IsFailure)
                {
                    // A definite rails refusal (4xx taxonomy) proves no money moved:
                    // sign the reversal so the case nets to zero; the claim releases
                    // for a bounded retry that starts a fresh case.
                    Result<RentalRefundMovementSnapshot> reversal = await recorder.RecordFailureAsync(
                        refundCaseId, award, actorUserId, utcNow, evidenceReference, cancellationToken);
                    if (reversal.IsFailure)
                        return RefundExecutionResult.KeepClaim(refundCaseId, destination, reversal.Error);

                    return RefundExecutionResult.RetryableFailure(refundCaseId, destination, refund.Error);
                }

                string? providerReference = string.IsNullOrWhiteSpace(refund.Value) ? null : refund.Value!.Trim();
                if (providerReference is null)
                {
                    // Accepted without a reference: the outcome cannot be confirmed
                    // from here (#16 — approval alone is not completion). Durable
                    // timeout, claim pending, operations reconciles.
                    Result<RentalRefundMovementSnapshot> timedOut = await recorder.RecordTimeoutAsync(
                        refundCaseId, award, actorUserId, utcNow,
                        providerSignal: "Paymob.RefundAcceptedWithoutReference", CancellationToken.None);
                    if (timedOut.IsFailure)
                        return RefundExecutionResult.KeepClaim(refundCaseId, destination, timedOut.Error);

                    return RefundExecutionResult.Blocked(refundCaseId, destination, RentalRefundErrors.OutcomePendingReconciliation);
                }

                Result<RentalRefundMovementSnapshot> completed = await recorder.CompleteTransferAsync(
                    refundCaseId, award, providerReference, actorUserId, utcNow, evidenceReference, cancellationToken);
                if (completed.IsFailure)
                    return RefundExecutionResult.KeepClaim(refundCaseId, destination, completed.Error);

                return RefundExecutionResult.Committed(
                    refundCaseId, claimedAmountMinorUnits, destination, providerReference, completed.Value.IsReplay);
            }

            default:
                return RefundExecutionResult.RetryableFailure(refundCaseId, destination, RentalRefundErrors.DestinationUnavailable);
        }
    }

    /// <summary>True when the provider error means "the call did not confirm what happened" rather than "the refund was refused" (the Families rails taxonomy: 4xx refusals are definitive; 5xx/transport/parse are not).</summary>
    private static bool IsOutcomeUnknown(Error? error) =>
        error is not null &&
        string.Equals(error.Code, "Paymob.RefundOutcomeUnknown", StringComparison.Ordinal);

    /// <summary>The executed or classified outcome of one refund movement.</summary>
    public enum RefundOutcome
    {
        /// <summary>The movement completed (freshly or as a replay) — the caller records the movement row.</summary>
        Committed,

        /// <summary>The outcome is unknown or blocked: the claim stays pending for operations; nothing may execute.</summary>
        BlockedUnknown,

        /// <summary>Verified non-movement: the claim may release and a retry may start fresh.</summary>
        FailedRetryable
    }

    public sealed record RefundExecutionResult(
        RefundOutcome Outcome,
        Guid RefundCaseId,
        long AwardedMinorUnits,
        RentalRefundDestination Destination,
        string? GatewayReference,
        bool IsReplay,
        Error? Failure)
    {
        internal static RefundExecutionResult Committed(Guid refundCaseId, long amount, RentalRefundDestination destination, string? gatewayReference, bool isReplay) =>
            new(RefundOutcome.Committed, refundCaseId, amount, destination, gatewayReference, isReplay, Failure: null);

        internal static RefundExecutionResult CommittedReplay(Guid refundCaseId, long amount, RentalRefundDestination destination, string? gatewayReference) =>
            new(RefundOutcome.Committed, refundCaseId, amount, destination, gatewayReference, IsReplay: true, Failure: null);

        internal static RefundExecutionResult Blocked(Guid refundCaseId, RentalRefundDestination destination, Error failure) =>
            new(RefundOutcome.BlockedUnknown, refundCaseId, 0, destination, GatewayReference: null, IsReplay: false, failure);

        internal static RefundExecutionResult RetryableFailure(Guid refundCaseId, RentalRefundDestination destination, Error failure) =>
            new(RefundOutcome.FailedRetryable, refundCaseId, 0, destination, GatewayReference: null, IsReplay: false, failure);

        internal static RefundExecutionResult KeepClaim(Guid refundCaseId, RentalRefundDestination destination, Error failure) =>
            new(RefundOutcome.BlockedUnknown, refundCaseId, 0, destination, GatewayReference: null, IsReplay: false, failure);
    }
}

/// <summary>
/// Family cancellation before delivery (clause 8: "Before delivery: full
/// refund"). An unpaid booking cancels with nothing to move; a captured one
/// refunds the whole captured rent plus deposit through the chosen destination.
/// A repeated cancel replays its stable result: a recorded movement replays the
/// refund outcome, an already-cancelled unpaid booking replays
/// <c>CancelledWithoutCapture</c>, and an in-flight refund claim resumes the
/// SAME persisted case (different content never erases the claim).
/// </summary>
public sealed record CancelRentalBookingCommand(
    Guid RentalBookingId,
    Guid RequestedByUserId,
    RentalRefundDestination Destination,
    string? IdempotencyKey,
    DateTime UtcNow) : ICommand<RentalBookingRefundResponse>;

public sealed record RentalBookingRefundResponse(
    Guid RentalBookingId,
    RentalBookingStatus Status,
    string Reason,
    RentalRefundMovementKind Kind,
    Guid RefundCaseId,
    RentalRefundDestination Destination,
    decimal Awarded,
    bool IsReplay);

public sealed class CancelRentalBookingCommandHandler(
    IRentalsDbContext dbContext,
    IFamiliesDbContext familiesDbContext,
    IPaymobClient paymobClient,
    IRentalRefundRecorder recorder)
    : ICommandHandler<CancelRentalBookingCommand, RentalBookingRefundResponse>
{
    public async Task<Result<RentalBookingRefundResponse>> Handle(
        CancelRentalBookingCommand request,
        CancellationToken cancellationToken)
    {
        if (!RentalRefundDestinationGuard.IsExecutable(request.Destination))
            return Result<RentalBookingRefundResponse>.Failure(RentalRefundErrors.DestinationUnavailable);

        // Input bound BEFORE any claim or Finance/provider call: an oversized
        // idempotency key would otherwise surface late, as a DomainException on a
        // half-executed movement (M2/Ask: stable 400, nothing mutated).
        if (request.IdempotencyKey is not null && request.IdempotencyKey.Length > RentalRefundExecutor.MaximumIdempotencyKeyLength)
            return Result<RentalBookingRefundResponse>.Failure(RentalRefundErrors.IdempotencyKeyTooLong);

        RentalBooking? booking = null;
        try
        {
            var family = await familiesDbContext.Families
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Members.Any(member => member.Id == new UserId(request.RequestedByUserId)), cancellationToken);
            if (family is null)
                return Result<RentalBookingRefundResponse>.Failure(RentalBookingErrors.FamilyNotFound);

            booking = await dbContext.RentalBookings
                .Include(candidate => candidate.PaymentTransactions)
                .Include(candidate => candidate.RefundMovements)
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == request.RentalBookingId && candidate.FamilyId == family.Id.Value,
                    cancellationToken);

            if (booking is null)
                return Result<RentalBookingRefundResponse>.Failure(RentalBookingErrors.NotFound);

            // Replay: the outcome row already exists — return it unchanged.
            if (booking.RefundMovementOf(RentalRefundMovementKind.CancellationFullRefund) is { } recorded)
                return Result<RentalBookingRefundResponse>.Success(
                    RentalRefundResponseMapper.MapReplay(booking, RentalRefundMovementKind.CancellationFullRefund, recorded, "AlreadyProcessed"));

            // Replay: an already-cancelled UNPAID booking (no claim in flight) is a
            // stable success — a repeated cancel must never surface as a conflict.
            if (booking.Status == RentalBookingStatus.CancelledBeforeDelivery &&
                booking.CapturedTotalMinorUnits == 0 && booking.PendingRefundCaseId is null)
                return Result<RentalBookingRefundResponse>.Success(
                    new RentalBookingRefundResponse(
                        booking.Id,
                        booking.Status,
                        "CancelledWithoutCapture",
                        RentalRefundMovementKind.CancellationFullRefund,
                        Guid.Empty,
                        request.Destination,
                        0m,
                        IsReplay: true));

            if (booking.PendingRefundKind is not null &&
                booking.PendingRefundKind != RentalRefundMovementKind.CancellationFullRefund)
            {
                // A different refund flow holds the claim; different content may not
                // erase it — refuse and leave the claim exactly as it stands.
                return Result<RentalBookingRefundResponse>.Failure(RentalRefundErrors.AlreadyRecorded);
            }

            // M3 destination binding: once a claim exists, ITS persisted destination
            // is authoritative — a resume never adopts a different re-request.
            RentalRefundDestination claimDestination = booking.PendingRefundDestination ?? request.Destination;

            if (booking.PendingRefundKind is null)
            {
                // A definitive failed refund releases its claim but preserves the
                // cancellation. Resume the paid cancellation without cancelling again.
                bool nothingToRefund = booking.Status != RentalBookingStatus.CancelledBeforeDelivery &&
                    booking.CancelBeforeDelivery(request.RequestedByUserId, request.UtcNow);
                if (nothingToRefund)
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return Result<RentalBookingRefundResponse>.Success(
                        new RentalBookingRefundResponse(
                            booking.Id,
                            booking.Status,
                            "CancelledWithoutCapture",
                            RentalRefundMovementKind.CancellationFullRefund,
                            Guid.Empty,
                            request.Destination,
                            0m,
                            IsReplay: false));
                }

                // Missing provider reference is validated BEFORE approval/claim.
                if (booking.CapturedTotalMinorUnits > 0 &&
                    claimDestination == RentalRefundDestination.OriginalPaymentMethod &&
                    booking.SucceededPaymentTransaction()?.PaymobTransactionId is not { Length: > 0 })
                {
                    return Result<RentalBookingRefundResponse>.Failure(RentalRefundErrors.NoPaymentReference);
                }

                booking.BeginRefund(
                    RentalRefundMovementKind.CancellationFullRefund,
                    booking.CapturedTotalMinorUnits,
                    Guid.CreateVersion7(),
                    claimDestination,
                    request.UtcNow);
                await dbContext.SaveChangesAsync(cancellationToken);
                claimDestination = booking.PendingRefundDestination!.Value;
            }

            string evidence = RentalRefundExecutor.ComposeEvidence(
                booking.Id, RentalRefundMovementKind.CancellationFullRefund, claimDestination, request.IdempotencyKey);

            RentalRefundExecutor.RefundExecutionResult execution = await RentalRefundExecutor.ExecuteOrResumeAsync(
                RentalRefundReason.CancellationBeforeDelivery,
                booking.PendingRefundCaseId!.Value,
                booking.PendingRefundAmountMinorUnits!.Value,
                booking.FamilyId,
                request.RequestedByUserId,
                request.UtcNow,
                evidence,
                booking.SucceededPaymentTransaction()?.PaymobTransactionId,
                request.IdempotencyKey,
                claimDestination,
                paymobClient,
                recorder,
                cancellationToken);

            return await RefundFinalizer.FinalizeAsync(
                booking, dbContext, execution,
                RentalRefundMovementKind.CancellationFullRefund,
                request.RequestedByUserId, request.UtcNow, evidence,
                successReason: "CancelledWithFullRefund",
                cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result<RentalBookingRefundResponse>.Failure(new Error(RentalBookingErrors.InvalidOperation.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<RentalBookingRefundResponse>.Failure(RentalBookingErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // B1: an in-flight claim must never die as a silent 500 — best-effort
            // durable timeout so operations can reconcile, then a stable blocked
            // result. The claim survives either way; nothing is released or re-sent.
            if (booking?.PendingRefundCaseId is { } pendingCase)
            {
                try
                {
                    await recorder.RecordTimeoutAsync(
                        pendingCase,
                        Money.FromMinorUnits(booking.PendingRefundAmountMinorUnits ?? 0),
                        request.RequestedByUserId,
                        request.UtcNow,
                        providerSignal: "handler:unhandled-exception",
                        CancellationToken.None);
                }
                catch
                {
                    // The fence record failed too; the claim stays and the next
                    // resume re-reads the standing approval into the blocked arm.
                }

                return Result<RentalBookingRefundResponse>.Failure(RentalRefundErrors.OutcomePendingReconciliation);
            }

            return Result<RentalBookingRefundResponse>.Failure(RentalBookingErrors.Unknown);
        }
    }
}

/// <summary>
/// The unused-whole-prepaid-days refund on early return (clause 8; #19: a
/// started 24-hour period counts as used, remaining whole prepaid periods
/// refund at the snapshotted rate). The award is always policy-derived — the
/// family only chooses the destination (#10), never an amount. Order-independent
/// with the deposit settlement: either flow may complete first (the settlement
/// never forecloses the unused-days refund and vice versa; #4's cumulative
/// ceiling still bounds the sum).
/// </summary>
public sealed record ClaimRentalUnusedDaysRefundCommand(
    Guid RentalBookingId,
    Guid RequestedByUserId,
    RentalRefundDestination Destination,
    string? IdempotencyKey,
    DateTime UtcNow) : ICommand<RentalBookingRefundResponse>;

public sealed class ClaimRentalUnusedDaysRefundCommandHandler(
    IRentalsDbContext dbContext,
    IFamiliesDbContext familiesDbContext,
    IPaymobClient paymobClient,
    IRentalRefundRecorder recorder)
    : ICommandHandler<ClaimRentalUnusedDaysRefundCommand, RentalBookingRefundResponse>
{
    public async Task<Result<RentalBookingRefundResponse>> Handle(
        ClaimRentalUnusedDaysRefundCommand request,
        CancellationToken cancellationToken)
    {
        if (!RentalRefundDestinationGuard.IsExecutable(request.Destination))
            return Result<RentalBookingRefundResponse>.Failure(RentalRefundErrors.DestinationUnavailable);

        // Input bound BEFORE any claim or Finance/provider call (stable 400, nothing mutated).
        if (request.IdempotencyKey is not null && request.IdempotencyKey.Length > RentalRefundExecutor.MaximumIdempotencyKeyLength)
            return Result<RentalBookingRefundResponse>.Failure(RentalRefundErrors.IdempotencyKeyTooLong);

        RentalBooking? booking = null;
        try
        {
            var family = await familiesDbContext.Families
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Members.Any(member => member.Id == new UserId(request.RequestedByUserId)), cancellationToken);
            if (family is null)
                return Result<RentalBookingRefundResponse>.Failure(RentalBookingErrors.FamilyNotFound);

            booking = await dbContext.RentalBookings
                .Include(candidate => candidate.PaymentTransactions)
                .Include(candidate => candidate.RefundMovements)
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == request.RentalBookingId && candidate.FamilyId == family.Id.Value,
                    cancellationToken);

            if (booking is null)
                return Result<RentalBookingRefundResponse>.Failure(RentalBookingErrors.NotFound);

            if (booking.RefundMovementOf(RentalRefundMovementKind.EarlyReturnUnusedDays) is { } recorded)
                return Result<RentalBookingRefundResponse>.Success(
                    RentalRefundResponseMapper.MapReplay(booking, RentalRefundMovementKind.EarlyReturnUnusedDays, recorded, "AlreadyProcessed"));

            if (booking.PendingRefundKind is not null &&
                booking.PendingRefundKind != RentalRefundMovementKind.EarlyReturnUnusedDays)
            {
                return Result<RentalBookingRefundResponse>.Failure(RentalRefundErrors.AlreadyRecorded);
            }

            // M3 destination binding: the claim's persisted destination is authoritative.
            RentalRefundDestination claimDestination = booking.PendingRefundDestination ?? request.Destination;

            if (booking.PendingRefundKind is null)
            {
                long derived = booking.ComputeEarlyReturnUnusedDaysAmount();

                // Missing provider reference is validated BEFORE approval/claim.
                if (derived > 0 &&
                    claimDestination == RentalRefundDestination.OriginalPaymentMethod &&
                    booking.SucceededPaymentTransaction()?.PaymobTransactionId is not { Length: > 0 })
                {
                    return Result<RentalBookingRefundResponse>.Failure(RentalRefundErrors.NoPaymentReference);
                }

                booking.BeginRefund(
                    RentalRefundMovementKind.EarlyReturnUnusedDays,
                    derived,
                    Guid.CreateVersion7(),
                    claimDestination,
                    request.UtcNow);
                await dbContext.SaveChangesAsync(cancellationToken);
                claimDestination = booking.PendingRefundDestination!.Value;
            }

            string evidence = RentalRefundExecutor.ComposeEvidence(
                booking.Id, RentalRefundMovementKind.EarlyReturnUnusedDays, claimDestination, request.IdempotencyKey);

            RentalRefundExecutor.RefundExecutionResult execution = await RentalRefundExecutor.ExecuteOrResumeAsync(
                RentalRefundReason.EarlyReturnUnusedDays,
                booking.PendingRefundCaseId!.Value,
                booking.PendingRefundAmountMinorUnits!.Value,
                booking.FamilyId,
                request.RequestedByUserId,
                request.UtcNow,
                evidence,
                booking.SucceededPaymentTransaction()?.PaymobTransactionId,
                request.IdempotencyKey,
                claimDestination,
                paymobClient,
                recorder,
                cancellationToken);

            return await RefundFinalizer.FinalizeAsync(
                booking, dbContext, execution,
                RentalRefundMovementKind.EarlyReturnUnusedDays,
                request.RequestedByUserId, request.UtcNow, evidence,
                successReason: "UnusedDaysRefunded",
                cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result<RentalBookingRefundResponse>.Failure(new Error(RentalBookingErrors.InvalidOperation.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<RentalBookingRefundResponse>.Failure(RentalBookingErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // B1: an in-flight claim must never die as a silent 500 (see the cancel
            // handler for the full rationale): best-effort durable timeout, then a
            // stable blocked result; the claim survives either way.
            if (booking?.PendingRefundCaseId is { } pendingCase)
            {
                try
                {
                    await recorder.RecordTimeoutAsync(
                        pendingCase,
                        Money.FromMinorUnits(booking.PendingRefundAmountMinorUnits ?? 0),
                        request.RequestedByUserId,
                        request.UtcNow,
                        providerSignal: "handler:unhandled-exception",
                        CancellationToken.None);
                }
                catch
                {
                    // The fence record failed too; the claim stays and the next
                    // resume re-reads the standing approval into the blocked arm.
                }

                return Result<RentalBookingRefundResponse>.Failure(RentalRefundErrors.OutcomePendingReconciliation);
            }

            return Result<RentalBookingRefundResponse>.Failure(RentalBookingErrors.Unknown);
        }
    }
}

/// <summary>
/// The deposit settlement (clause 8: deposit returned within seven business
/// days of receipt and inspection, less documented damage; #20 deadline; #21
/// approved deductions). The netting amount is policy-derived and frozen on the
/// claim; the ops actor chooses the destination. Settlement after the deadline
/// releases the deposit in full — the hold window cannot be extended.
/// </summary>
public sealed record SettleRentalDepositCommand(
    Guid RentalBookingId,
    Guid ActorUserId,
    RentalRefundDestination Destination,
    string? IdempotencyKey,
    DateTime UtcNow) : ICommand<RentalBookingRefundResponse>;

public sealed class SettleRentalDepositCommandHandler(
    IRentalsDbContext dbContext,
    IPaymobClient paymobClient,
    IRentalRefundRecorder recorder)
    : ICommandHandler<SettleRentalDepositCommand, RentalBookingRefundResponse>
{
    public async Task<Result<RentalBookingRefundResponse>> Handle(
        SettleRentalDepositCommand request,
        CancellationToken cancellationToken)
    {
        if (!RentalRefundDestinationGuard.IsExecutable(request.Destination))
            return Result<RentalBookingRefundResponse>.Failure(RentalRefundErrors.DestinationUnavailable);

        // Input bound BEFORE any claim or Finance/provider call (stable 400, nothing mutated).
        if (request.IdempotencyKey is not null && request.IdempotencyKey.Length > RentalRefundExecutor.MaximumIdempotencyKeyLength)
            return Result<RentalBookingRefundResponse>.Failure(RentalRefundErrors.IdempotencyKeyTooLong);

        RentalBooking? booking = null;
        try
        {
            booking = await dbContext.RentalBookings
                .Include(candidate => candidate.DamageDeductions)
                .Include(candidate => candidate.PaymentTransactions)
                .Include(candidate => candidate.RefundMovements)
                .SingleOrDefaultAsync(candidate => candidate.Id == request.RentalBookingId, cancellationToken);

            if (booking is null)
                return Result<RentalBookingRefundResponse>.Failure(RentalBookingErrors.NotFound);

            if (booking.RefundMovementOf(RentalRefundMovementKind.DepositSettlement) is { } recorded)
                return Result<RentalBookingRefundResponse>.Success(
                    RentalRefundResponseMapper.MapReplay(booking, RentalRefundMovementKind.DepositSettlement, recorded, "AlreadyProcessed"));

            if (booking.PendingRefundKind is not null &&
                booking.PendingRefundKind != RentalRefundMovementKind.DepositSettlement)
            {
                return Result<RentalBookingRefundResponse>.Failure(RentalRefundErrors.AlreadyRecorded);
            }

            // M3 destination binding: the claim's persisted destination is authoritative.
            RentalRefundDestination claimDestination = booking.PendingRefundDestination ?? request.Destination;

            if (booking.PendingRefundKind is null)
            {
                long derived = booking.ComputeDepositSettlementAmount(request.UtcNow);

                // Missing provider reference is validated BEFORE approval/claim.
                if (derived > 0 &&
                    claimDestination == RentalRefundDestination.OriginalPaymentMethod &&
                    booking.SucceededPaymentTransaction()?.PaymobTransactionId is not { Length: > 0 })
                {
                    return Result<RentalBookingRefundResponse>.Failure(RentalRefundErrors.NoPaymentReference);
                }

                booking.BeginRefund(
                    RentalRefundMovementKind.DepositSettlement,
                    derived,
                    Guid.CreateVersion7(),
                    claimDestination,
                    request.UtcNow);
                await dbContext.SaveChangesAsync(cancellationToken);
                claimDestination = booking.PendingRefundDestination!.Value;
            }

            string evidence = RentalRefundExecutor.ComposeEvidence(
                booking.Id, RentalRefundMovementKind.DepositSettlement, claimDestination, request.IdempotencyKey);

            RentalRefundExecutor.RefundExecutionResult execution = await RentalRefundExecutor.ExecuteOrResumeAsync(
                RentalRefundReason.DepositSettlement,
                booking.PendingRefundCaseId!.Value,
                booking.PendingRefundAmountMinorUnits!.Value,
                booking.FamilyId,
                request.ActorUserId,
                request.UtcNow,
                evidence,
                booking.SucceededPaymentTransaction()?.PaymobTransactionId,
                request.IdempotencyKey,
                claimDestination,
                paymobClient,
                recorder,
                cancellationToken);

            return await RefundFinalizer.FinalizeAsync(
                booking, dbContext, execution,
                RentalRefundMovementKind.DepositSettlement,
                request.ActorUserId, request.UtcNow, evidence,
                successReason: "DepositSettled",
                cancellationToken,
                markDepositSettled: true);
        }
        catch (DomainException ex)
        {
            return Result<RentalBookingRefundResponse>.Failure(new Error(RentalBookingErrors.InvalidOperation.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<RentalBookingRefundResponse>.Failure(RentalBookingErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // B1: an in-flight claim must never die as a silent 500 (see the cancel
            // handler for the full rationale): best-effort durable timeout, then a
            // stable blocked result; the claim survives either way.
            if (booking?.PendingRefundCaseId is { } pendingCase)
            {
                try
                {
                    await recorder.RecordTimeoutAsync(
                        pendingCase,
                        Money.FromMinorUnits(booking.PendingRefundAmountMinorUnits ?? 0),
                        request.ActorUserId,
                        request.UtcNow,
                        providerSignal: "handler:unhandled-exception",
                        CancellationToken.None);
                }
                catch
                {
                    // The fence record failed too; the claim stays and the next
                    // resume re-reads the standing approval into the blocked arm.
                }

                return Result<RentalBookingRefundResponse>.Failure(RentalRefundErrors.OutcomePendingReconciliation);
            }

            return Result<RentalBookingRefundResponse>.Failure(RentalBookingErrors.Unknown);
        }
    }
}

/// <summary>
/// Shared tail of the three refund drivers: lands the movement row on a committed
/// (or replayed) outcome — releasing the claim on a verified non-movement, and
/// leaving a blocked outcome's claim pending for operations. For the deposit
/// settlement the terminal <c>DepositSettled</c> status rides the same unit of
/// work as the movement row.
/// </summary>
internal static class RefundFinalizer
{
    public static async Task<Result<RentalBookingRefundResponse>> FinalizeAsync(
        RentalBooking booking,
        IRentalsDbContext dbContext,
        RentalRefundExecutor.RefundExecutionResult execution,
        RentalRefundMovementKind kind,
        Guid actorUserId,
        DateTime utcNow,
        string evidence,
        string successReason,
        CancellationToken cancellationToken,
        bool markDepositSettled = false)
    {
        switch (execution.Outcome)
        {
            case RentalRefundExecutor.RefundOutcome.Committed:
                booking.CompleteRefund(
                    execution.Destination,
                    actorUserId,
                    execution.GatewayReference,
                    evidence,
                    utcNow);

                if (markDepositSettled)
                    booking.MarkDepositSettled(utcNow, actorUserId);

                await dbContext.SaveChangesAsync(cancellationToken);
                return Result<RentalBookingRefundResponse>.Success(
                    new RentalBookingRefundResponse(
                        booking.Id,
                        booking.Status,
                        successReason,
                        kind,
                        execution.RefundCaseId,
                        execution.Destination,
                        execution.AwardedMinorUnits / 100m,
                        execution.IsReplay));

            case RentalRefundExecutor.RefundOutcome.FailedRetryable:
                booking.ReleaseRefundClaim(utcNow);
                await dbContext.SaveChangesAsync(cancellationToken);
                return Result<RentalBookingRefundResponse>.Failure(
                    execution.Failure ?? RentalRefundErrors.AttemptFailedRetryable);

            default:
                // Blocked/unknown: the claim stays pending and untouched so the
                // operations reconciliation can classify and finish it (#17).
                return Result<RentalBookingRefundResponse>.Failure(
                    execution.Failure ?? RentalRefundErrors.OutcomePendingReconciliation);
        }
    }
}

/// <summary>The recorded outcome row replays as the stable refund response.</summary>
internal static class RentalRefundResponseMapper
{
    public static RentalBookingRefundResponse MapReplay(
        RentalBooking booking,
        RentalRefundMovementKind kind,
        RentalRefundMovement movement,
        string reason) => new(
        booking.Id,
        booking.Status,
        reason,
        kind,
        movement.RefundCaseId,
        movement.Destination,
        movement.AmountMinorUnits / 100m,
        IsReplay: true);
}
