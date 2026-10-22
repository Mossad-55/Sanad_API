using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Finance.Application;

/// <summary>
/// Why a rental money movement is being recorded. The three clause 8 flows
/// stay separately attributable in the ledger, like the contract requires for
/// every refund family: a cancellation before delivery refunds everything
/// captured, an early return refunds the unused whole prepaid periods (#19),
/// and the deposit settlement refunds the deposit less approved on-time
/// deductions (#20, #21).
/// </summary>
public enum RentalRefundReason
{
    /// <summary>Clause 8: cancellation before delivery — full refund of the captured rent plus deposit.</summary>
    CancellationBeforeDelivery = 1,

    /// <summary>Clause 8 / decision #19: the unused whole prepaid 24-hour periods on early return.</summary>
    EarlyReturnUnusedDays = 2,

    /// <summary>Clause 8: the deposit returned within seven business days of receipt and inspection, less documented damage (#20, #21).</summary>
    DepositSettlement = 3
}

public static class RentalRefundErrors
{
    public static readonly Error CaseRequired =
        new("Finance.RentalRefund.CaseRequired", "A refund case is required.");

    public static readonly Error AwardInvalid =
        new("Finance.RentalRefund.AwardInvalid", "The rental refund award is invalid.");

    public static readonly Error Invalid =
        new("Finance.RentalRefund.Invalid", "The rental refund movement is not valid.");

    public static readonly Error ReconciliationRequired =
        new("Finance.RentalRefund.ReconciliationRequired", "The refund case must be reconciled from a recorded timeout before this outcome can be recorded.");

    public static readonly Error IdempotencyConflict =
        new("Finance.RentalRefund.IdempotencyConflict", "This refund case was already recorded with different content.");

    public static readonly Error Conflict =
        new("Finance.RentalRefund.Conflict", "The rental refund case was changed concurrently; retry.");

    public static readonly Error Unknown =
        new("Finance.RentalRefund.Unknown", "The rental refund movement could not be recorded.");
}

/// <summary>
/// The recorded outcome of one rental refund movement.
/// </summary>
public sealed record RentalRefundMovementSnapshot(
    Guid RefundCaseId,
    long AwardedMinorUnits,
    string Currency,
    RentalRefundReason Reason,
    bool IsReplay,
    RefundLedgerEntryType EntryType,
    DateTime RecordedOnUtc);

/// <summary>
/// W3-F4 additive seam: records rental refund movements on the Finance refund
/// ledger. The purchasable owner (the Rentals module, W3 design pin item 3)
/// computes the award from its own snapshots and correlation rows and drives
/// this seam for every movement; the ledger never learns about rental
/// aggregates and Rentals never writes Finance tables directly.
/// <para>
/// The safe ordering this seam enforces for payment-rails refunds: the award is
/// recorded (<see cref="IRentalRefundRecorder.RecordTransferAsync"/> with no
/// provider reference — an approval), <em>then</em> the payment rails are
/// called, and the outcome is completed (<see
/// cref="IRentalRefundRecorder.CompleteTransferAsync"/>), reversed (<see
/// cref="IRentalRefundRecorder.RecordFailureAsync"/> — only for a definitive
/// refusal), or durably timed out (<see
/// cref="IRentalRefundRecorder.RecordTimeoutAsync"/>, Owner decision #17) until
/// an evidence-carrying <see cref="IRentalRefundRecorder.ReconcileTimeoutAsync"/>
/// resolves it. Any retry classifies the stored case first (see
/// <see cref="IRentalRefundRecorder.ReadCaseAsync"/>) instead of blindly moving
/// money again.
/// </para>
/// <para>
/// App credit (Owner decision #15) is a single atomic Finance unit of work per
/// case through <see cref="IRentalRefundRecorder.PostRentalRefundCreditAsync"/>:
/// the balance movement and its CreditBalancePosted ledger entry commit
/// together, exactly once per refund case.
/// </para>
/// <para>
/// Idempotency is enforced at the case level without a receipts table (design
/// pin item 4): the evidence composes
/// <c>reason:&lt;token&gt;;key:&lt;idempotencyKey&gt;;ref:&lt;caller reference&gt;</c>;
/// a retry replaying the same evidence and amount on the same case returns the
/// original outcome; the same case with different content is the stable
/// <see cref="RentalRefundErrors.IdempotencyConflict"/>. The caller owns the
/// per-booking outcome rows (its unique <c>(booking, movement-kind)</c>
/// boundary) and the Finance ledger owns the money-movement truth (#35).
/// </para>
/// </summary>
public interface IRentalRefundRecorder
{
    /// <summary>Records the award (approval). Without a provider reference this is approval-only: #16 — approval alone is not completion.</summary>
    Task<Result<RentalRefundMovementSnapshot>> RecordTransferAsync(
        Guid refundCaseId,
        Money awarded,
        RentalRefundReason reason,
        Guid actorUserId,
        DateTime recordedOnUtc,
        string? idempotencyKey,
        string? providerReference,
        string? evidenceReference,
        CancellationToken cancellationToken);

    /// <summary>Completes an approved case with the provider/transfer reference and NON-EMPTY completion evidence (#16).</summary>
    Task<Result<RentalRefundMovementSnapshot>> CompleteTransferAsync(
        Guid refundCaseId,
        Money amount,
        string providerReference,
        Guid actorUserId,
        DateTime completedOnUtc,
        string evidenceReference,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reverses an approved case whose payment-rails attempt definitely failed: the only signed entry, netting the award to zero. Also accepts a case already reconciled out of a timeout (the zero-amount reconciliation marker) so verified nonpayment can be accounted after reconciliation.
    /// <para>Frozen-domain limitation (queued amendment): the aggregate's <c>Fail</c> carries no evidence slot, so <paramref name="evidenceReference"/> is accepted for interface stability and audited at the call site; the case-level content stays on the approval entry. Never pass provider-ranked data here.</para>
    /// </summary>
    Task<Result<RentalRefundMovementSnapshot>> RecordFailureAsync(
        Guid refundCaseId,
        Money failedAmount,
        Guid? actorUserId,
        DateTime failedOnUtc,
        string? evidenceReference,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the recorded timeline of one refund case, or null when nothing was ever
    /// recorded under the case id. The purchasable owner's resume path classifies the
    /// stored outcome from this timeline (Owner decision #17: an ambiguous outcome is
    /// reconciled, never blindly retried) instead of re-executing money.
    /// </summary>
    Task<Result<RentalRefundCaseSnapshot?>> ReadCaseAsync(
        Guid refundCaseId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records an ambiguous provider outcome durably (Owner decision #17): the case
    /// blocks every further transition until an explicit, evidence-carrying
    /// reconciliation runs. This is never a signed reversal — the money may or may
    /// not have moved. Re-invoking on an already-timed-out case replays the recorded
    /// timeout instead of appending a second one.
    /// </summary>
    Task<Result<RentalRefundMovementSnapshot>> RecordTimeoutAsync(
        Guid refundCaseId,
        Money ambiguousAmount,
        Guid? actorUserId,
        DateTime timedOutOnUtc,
        string? providerSignal,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reconciles a previously recorded timeout with authenticated evidence and an
    /// optional provider reference (Owner decision #17). The reconciliation itself
    /// moves no money; the actual accounting follows through
    /// <see cref="CompleteTransferAsync"/> (verified success) or
    /// <see cref="RecordFailureAsync"/> (verified nonpayment).
    /// </summary>
    Task<Result<RentalRefundMovementSnapshot>> ReconcileTimeoutAsync(
        Guid refundCaseId,
        Guid actorUserId,
        string evidenceReference,
        string? providerReference,
        DateTime reconciledOnUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Rental-case-scoped, idempotent app-credit posting inside the Finance
    /// persistence boundary (Owner decision #15, #16, #31), M1 closure: in ONE
    /// Finance unit of work the case records the authoritative
    /// <see cref="RefundLedgerEntryType.Approved"/> entry and the attributable
    /// <see cref="RefundLedgerEntryType.CreditBalancePosted"/> entry under the
    /// booking's refund case id, plus the balance movement. The ledger is always
    /// opened under the REFUND CASE id (never the balance id). Replay validates
    /// case + family + amount + evidence (the proof token family-tags the
    /// Rentals-composed evidence — booking, kind, destination, idempotency key),
    /// never the amount alone. Exactly-once is enforced by the per-ledger append
    /// sequence's unique (refund case, sequence) index plus the balance's
    /// concurrency token: a loser's SaveChanges fails whole and its retry replays.
    /// </summary>
    Task<Result<RentalRefundMovementSnapshot>> PostRentalRefundCreditAsync(
        Guid refundCaseId,
        Guid familyId,
        Money amount,
        Guid actorUserId,
        DateTime occurredOnUtc,
        string? evidenceReference,
        CancellationToken cancellationToken);
}

/// <summary>One recorded entry of a refund case, for outcome classification on the caller's resume path.</summary>
public sealed record RentalRefundCaseEntrySnapshot(
    long Sequence,
    RefundLedgerEntryType EntryType,
    long AmountMinorUnits,
    string? ProviderReference,
    string? EvidenceReference,
    DateTime OccurredOnUtc);

/// <summary>The recorded state of one refund case. Null latest entries mean nothing was ever recorded.</summary>
public sealed record RentalRefundCaseSnapshot(
    Guid RefundCaseId,
    RefundLedgerEntryType? LatestEntryType,
    long LatestAmountMinorUnits,
    string? LatestProviderReference,
    long NetRecordedMinorUnits,
    long TotalCompletedMinorUnits,
    IReadOnlyList<RentalRefundCaseEntrySnapshot> Entries);

/// <summary>
/// The only post-timeout state an ops completion or reversal may act on: the
/// zero-amount reconciliation marker, which proves an operator already produced
/// evidence for the ambiguity. A plain in-flight approval is deliberately not
/// eligible — acting on it would race the automatic provider call.
/// </summary>
internal static class RentalRefundCaseState
{
    internal static bool IsReconciledOutcomePending(RentalRefundCaseSnapshot existing) =>
        existing.LatestEntryType == RefundLedgerEntryType.Failed &&
        existing.LatestAmountMinorUnits == 0 &&
        existing.Entries.Any(entry => entry.EntryType == RefundLedgerEntryType.TimedOut);
}

public sealed record RecordRentalRefundCommand(
    Guid RefundCaseId,
    long AwardedMinorUnits,
    RentalRefundReason Reason,
    Guid ActorUserId,
    DateTime RecordedOnUtc,
    string? IdempotencyKey = null,
    string? ProviderReference = null,
    string? EvidenceReference = null) : ICommand<RentalRefundMovementSnapshot>;

public sealed class RecordRentalRefundCommandHandler(IRentalRefundRecorder recorder)
    : ICommandHandler<RecordRentalRefundCommand, RentalRefundMovementSnapshot>
{
    public async Task<Result<RentalRefundMovementSnapshot>> Handle(
        RecordRentalRefundCommand request,
        CancellationToken cancellationToken)
    {
        if (request.RefundCaseId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.CaseRequired);
        if (request.ActorUserId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);
        if (request.AwardedMinorUnits < 0)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.AwardInvalid);
        if (request.RecordedOnUtc.Kind != DateTimeKind.Utc)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.AwardInvalid);
        if (!Enum.IsDefined(request.Reason))
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

        return await recorder.RecordTransferAsync(
            request.RefundCaseId,
            Money.FromMinorUnits(request.AwardedMinorUnits),
            request.Reason,
            request.ActorUserId,
            request.RecordedOnUtc,
            request.IdempotencyKey,
            request.ProviderReference,
            request.EvidenceReference,
            cancellationToken);
    }
}

/// <summary>
/// Ops surface: records an ambiguous provider outcome durably so the case blocks until reconciled (#17).
/// The timeout amount is DERIVED from the case's standing approval — the caller never names an amount,
/// and the seam re-validates the derived value against the recorded award. Role enforcement happens at
/// the host route; this seam cannot authenticate and deliberately carries no role parameter.
/// </summary>
public sealed record RecordRentalRefundTimeoutCommand(
    Guid RefundCaseId,
    Guid ActorUserId,
    DateTime TimedOutOnUtc,
    string? ProviderSignal = null) : ICommand<RentalRefundMovementSnapshot>;

public sealed class RecordRentalRefundTimeoutCommandHandler(IRentalRefundRecorder recorder)
    : ICommandHandler<RecordRentalRefundTimeoutCommand, RentalRefundMovementSnapshot>
{
    public async Task<Result<RentalRefundMovementSnapshot>> Handle(
        RecordRentalRefundTimeoutCommand request,
        CancellationToken cancellationToken)
    {
        if (request.RefundCaseId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.CaseRequired);
        if (request.ActorUserId == Guid.Empty || request.TimedOutOnUtc.Kind != DateTimeKind.Utc)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

        // Bind case + award: derive the amount from the case's standing approval
        // (an already-timed-out case still carries the approval that set it).
        Result<RentalRefundCaseSnapshot?> read = await recorder.ReadCaseAsync(request.RefundCaseId, cancellationToken);
        if (read.IsFailure)
            return Result<RentalRefundMovementSnapshot>.Failure(read.Error);
        if (read.Value is not { } existing)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

        long? standingAward = existing.Entries
            .Where(entry => entry.EntryType == RefundLedgerEntryType.Approved)
            .OrderBy(entry => entry.Sequence)
            .LastOrDefault()?.AmountMinorUnits;

        if (standingAward is null)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

        return await recorder.RecordTimeoutAsync(
            request.RefundCaseId,
            Money.FromMinorUnits(standingAward.Value),
            request.ActorUserId,
            request.TimedOutOnUtc,
            request.ProviderSignal,
            cancellationToken);
    }
}

/// <summary>Ops surface: reconciles a recorded timeout with authenticated evidence — the only transition an unresolved timeout allows (#17). Moves no money; the outcome accounting follows through <see cref="CompleteRentalRefundCommand"/> (verified success) or <see cref="ReverseRentalRefundCommand"/> (verified nonpayment).</summary>
public sealed record ReconcileRentalRefundTimeoutCommand(
    Guid RefundCaseId,
    Guid ActorUserId,
    string EvidenceReference,
    DateTime ReconciledOnUtc,
    string? ProviderReference = null) : ICommand<RentalRefundMovementSnapshot>;

public sealed class ReconcileRentalRefundTimeoutCommandHandler(IRentalRefundRecorder recorder)
    : ICommandHandler<ReconcileRentalRefundTimeoutCommand, RentalRefundMovementSnapshot>
{
    public async Task<Result<RentalRefundMovementSnapshot>> Handle(
        ReconcileRentalRefundTimeoutCommand request,
        CancellationToken cancellationToken)
    {
        if (request.RefundCaseId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.CaseRequired);
        if (request.ActorUserId == Guid.Empty ||
            request.ReconciledOnUtc.Kind != DateTimeKind.Utc ||
            string.IsNullOrWhiteSpace(request.EvidenceReference))
        {
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);
        }

        return await recorder.ReconcileTimeoutAsync(
            request.RefundCaseId,
            request.ActorUserId,
            request.EvidenceReference,
            request.ProviderReference,
            request.ReconciledOnUtc,
            cancellationToken);
    }
}

/// <summary>Ops surface: accounts a VERIFIED successful transfer on a case whose recorded timeout has been reconciled with evidence, with the mandatory provider reference and NON-EMPTY completion evidence. A plain in-flight approval is refused so ops cannot race the automatic provider call; an already-confirmed case replays. The amount is an expected-award assertion — the seam validates it against the recorded approval.</summary>
public sealed record CompleteRentalRefundCommand(
    Guid RefundCaseId,
    long AmountMinorUnits,
    string ProviderReference,
    Guid ActorUserId,
    DateTime CompletedOnUtc,
    string EvidenceReference) : ICommand<RentalRefundMovementSnapshot>;

public sealed class CompleteRentalRefundCommandHandler(IRentalRefundRecorder recorder)
    : ICommandHandler<CompleteRentalRefundCommand, RentalRefundMovementSnapshot>
{
    public async Task<Result<RentalRefundMovementSnapshot>> Handle(
        CompleteRentalRefundCommand request,
        CancellationToken cancellationToken)
    {
        if (request.RefundCaseId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.CaseRequired);
        if (request.ActorUserId == Guid.Empty ||
            request.CompletedOnUtc.Kind != DateTimeKind.Utc ||
            string.IsNullOrWhiteSpace(request.ProviderReference) ||
            string.IsNullOrWhiteSpace(request.EvidenceReference))
        {
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);
        }

        Result<RentalRefundCaseSnapshot?> read = await recorder.ReadCaseAsync(request.RefundCaseId, cancellationToken);
        if (read.IsFailure)
            return Result<RentalRefundMovementSnapshot>.Failure(read.Error);
        if (read.Value is not { } existing)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

        // The automatic executor owns an in-flight approval (it records the
        // approval, then calls the provider). An ops completion on that plain
        // approval would race the live call, so it is allowed only for an
        // idempotent replay of a confirmed transfer or after reconciliation
        // produced the evidence-carrying zero marker.
        if (existing.LatestEntryType != RefundLedgerEntryType.TransferConfirmed &&
            !RentalRefundCaseState.IsReconciledOutcomePending(existing))
        {
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.ReconciliationRequired);
        }

        return await recorder.CompleteTransferAsync(
            request.RefundCaseId,
            Money.FromMinorUnits(request.AmountMinorUnits),
            request.ProviderReference,
            request.ActorUserId,
            request.CompletedOnUtc,
            request.EvidenceReference,
            cancellationToken);
    }
}

/// <summary>Ops surface: accounts VERIFIED nonpayment after reconciliation — the only signed reversal, netting the case to zero so a retry can start a fresh case. A plain in-flight approval is refused so ops cannot release a claim while the automatic provider call is still live.</summary>
public sealed record ReverseRentalRefundCommand(
    Guid RefundCaseId,
    long FailedAmountMinorUnits,
    Guid ActorUserId,
    DateTime FailedOnUtc,
    string? EvidenceReference = null) : ICommand<RentalRefundMovementSnapshot>;

public sealed class ReverseRentalRefundCommandHandler(IRentalRefundRecorder recorder)
    : ICommandHandler<ReverseRentalRefundCommand, RentalRefundMovementSnapshot>
{
    public async Task<Result<RentalRefundMovementSnapshot>> Handle(
        ReverseRentalRefundCommand request,
        CancellationToken cancellationToken)
    {
        if (request.RefundCaseId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.CaseRequired);
        if (request.ActorUserId == Guid.Empty || request.FailedOnUtc.Kind != DateTimeKind.Utc)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

        Result<RentalRefundCaseSnapshot?> read = await recorder.ReadCaseAsync(request.RefundCaseId, cancellationToken);
        if (read.IsFailure)
            return Result<RentalRefundMovementSnapshot>.Failure(read.Error);
        if (read.Value is not { } existing)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);
        if (!RentalRefundCaseState.IsReconciledOutcomePending(existing))
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.ReconciliationRequired);

        return await recorder.RecordFailureAsync(
            request.RefundCaseId,
            Money.FromMinorUnits(request.FailedAmountMinorUnits),
            request.ActorUserId,
            request.FailedOnUtc,
            request.EvidenceReference,
            cancellationToken);
    }
}

/// <summary>
/// Finance-side persistence over <see cref="IFinanceDbContext"/>. The ledger
/// records the MOVEMENT (Owner decision #35): the awarded amount, never a
/// resulting balance.
/// </summary>
public sealed class RentalRefundRecorder(IFinanceDbContext dbContext) : IRentalRefundRecorder
{
    public async Task<Result<RentalRefundMovementSnapshot>> RecordTransferAsync(
        Guid refundCaseId,
        Money awarded,
        RentalRefundReason reason,
        Guid actorUserId,
        DateTime recordedOnUtc,
        string? idempotencyKey,
        string? providerReference,
        string? evidenceReference,
        CancellationToken cancellationToken)
    {
        if (refundCaseId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.CaseRequired);
        if (awarded.IsNegative)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.AwardInvalid);
        if (!Enum.IsDefined(reason))
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);
        if (actorUserId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

        try
        {
            string reference = ComposeEvidence(reason, idempotencyKey, evidenceReference);
            string? provider = string.IsNullOrWhiteSpace(providerReference) ? null : providerReference!.Trim();

            RefundLedger? existing = await LoadCaseAsync(refundCaseId, cancellationToken);

            if (existing is not null && FirstMovementEntry(existing) is { } recorded)
            {
                bool contentMatches =
                    recorded.Amount.MinorUnits == awarded.MinorUnits &&
                    recorded.EvidenceReference == reference &&
                    HasExpectedShape(existing, provider);

                return contentMatches
                    ? Result<RentalRefundMovementSnapshot>.Success(
                        ToSnapshot(refundCaseId, existing.LatestEntry!, reason, isReplay: true))
                    : Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.IdempotencyConflict);
            }

            RefundLedger ledger = existing ?? RefundLedger.Open(refundCaseId);
            if (existing is null)
                dbContext.RefundLedgers.Add(ledger);

            RefundLedgerEntry approved = ledger.Approve(awarded, recordedOnUtc, actorUserId, reference);
            bool completes = provider is not null && !awarded.IsZero;
            if (completes)
                ledger.ConfirmTransfer(awarded, recordedOnUtc, actorUserId, provider!, reference);

            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<RentalRefundMovementSnapshot>.Success(
                ToSnapshot(refundCaseId, completes ? ledger.LatestEntry! : approved, reason, isReplay: false));
        }
        catch (DomainException ex)
        {
            return Result<RentalRefundMovementSnapshot>.Failure(new Error(RentalRefundErrors.Invalid.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Conflict);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Two concurrent recorders raced to open the same case; the loser
            // retries and the case-level replay re-reads the winner's outcome.
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Unknown);
        }
    }

    public async Task<Result<RentalRefundMovementSnapshot>> CompleteTransferAsync(
        Guid refundCaseId,
        Money amount,
        string providerReference,
        Guid actorUserId,
        DateTime completedOnUtc,
        string evidenceReference,
        CancellationToken cancellationToken)
    {
        if (refundCaseId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.CaseRequired);
        if (amount.IsNegative)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.AwardInvalid);
        string provider = providerReference?.Trim() ?? string.Empty;
        if (provider.Length == 0)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);
        // #16: a confirmed transfer requires non-empty completion evidence — an
        // unverifiable "done" is exactly the ambiguity this seam exists to prevent.
        if (string.IsNullOrWhiteSpace(evidenceReference))
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);
        if (actorUserId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

        try
        {
            RefundLedger? existing = await LoadCaseAsync(refundCaseId, cancellationToken);
            if (existing is null)
                return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

            RefundLedgerEntry? latest = existing.LatestEntry;
            if (latest is null)
                return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

            if (latest.EntryType == RefundLedgerEntryType.TransferConfirmed)
            {
                if (latest.Amount.MinorUnits != amount.MinorUnits)
                    return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

                // The completion already landed (a raced or repeated driver):
                // replay the recorded outcome instead of a second entry.
                return Result<RentalRefundMovementSnapshot>.Success(
                    ToSnapshot(refundCaseId, latest, ReasonFromReference(latest.EvidenceReference), isReplay: true));
            }

            if (latest.EntryType != RefundLedgerEntryType.Approved &&
                !IsReconciliationMarker(existing, latest))
                return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

            if (latest.EntryType == RefundLedgerEntryType.Approved)
            {
                if (latest.Amount.MinorUnits != amount.MinorUnits)
                    return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);
            }
            else
            {
                // Completion after a timeout reconciliation: the case stands at the
                // zero-amount reconciliation marker; the confirmed award must equal
                // the case's standing approved award.
                if (LatestAwardEntry(existing) is not { } standingApproval ||
                    standingApproval.Amount.MinorUnits != amount.MinorUnits)
                    return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);
            }

            RefundLedgerEntry confirmed = existing.ConfirmTransfer(
                amount, completedOnUtc, actorUserId, provider, evidenceReference);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Result<RentalRefundMovementSnapshot>.Success(
                ToSnapshot(refundCaseId, confirmed, ReasonFromReference(latest.EvidenceReference), isReplay: false));
        }
        catch (DomainException ex)
        {
            return Result<RentalRefundMovementSnapshot>.Failure(new Error(RentalRefundErrors.Invalid.Code, ex.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Unknown);
        }
    }

    public async Task<Result<RentalRefundMovementSnapshot>> RecordFailureAsync(
        Guid refundCaseId,
        Money failedAmount,
        Guid? actorUserId,
        DateTime failedOnUtc,
        string? evidenceReference,
        CancellationToken cancellationToken)
    {
        if (refundCaseId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.CaseRequired);
        if (failedAmount.IsNegative)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.AwardInvalid);

        try
        {
            RefundLedger? existing = await LoadCaseAsync(refundCaseId, cancellationToken);
            if (existing is null)
                return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

            RefundLedgerEntry? latest = existing.LatestEntry;
            if (latest is null)
                return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

            // The standing award may be a plain approval (definite refusal reverses
            // immediately) or a case already reconciled out of a timeout (verified
            // nonpayment is accounted only after the evidence-carrying reconciliation).
            bool standsAtApproval = latest.EntryType == RefundLedgerEntryType.Approved &&
                latest.Amount.MinorUnits == failedAmount.MinorUnits;
            bool standsAtReconciliation = IsReconciliationMarker(existing, latest) &&
                LatestAwardEntry(existing)?.Amount.MinorUnits == failedAmount.MinorUnits;

            if (!standsAtApproval && !standsAtReconciliation)
                return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

            RefundLedgerEntry failed = existing.Fail(
                failedAmount.Negate(), failedOnUtc, actorUserId, providerReference: null);
            // Frozen-domain limitation (queued amendment): the aggregate's Fail has no
            // evidence slot — the case-level content remains on the approval entry, and
            // the reversal's own evidence surfaces through the ops audit trail only.
            await dbContext.SaveChangesAsync(cancellationToken);

            return Result<RentalRefundMovementSnapshot>.Success(
                ToSnapshot(refundCaseId, failed, ReasonFromReference(latest.EvidenceReference), isReplay: false));
        }
        catch (DomainException ex)
        {
            return Result<RentalRefundMovementSnapshot>.Failure(new Error(RentalRefundErrors.Invalid.Code, ex.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Unknown);
        }
    }

    public async Task<Result<RentalRefundCaseSnapshot?>> ReadCaseAsync(
        Guid refundCaseId,
        CancellationToken cancellationToken)
    {
        if (refundCaseId == Guid.Empty)
            return Result<RentalRefundCaseSnapshot?>.Failure(RentalRefundErrors.CaseRequired);

        try
        {
            RefundLedger? existing = await LoadCaseAsync(refundCaseId, cancellationToken);
            if (existing is null)
                return Result<RentalRefundCaseSnapshot?>.Success(null);

            RefundLedgerEntry? latest = existing.LatestEntry;
            return Result<RentalRefundCaseSnapshot?>.Success(new RentalRefundCaseSnapshot(
                refundCaseId,
                latest?.EntryType,
                latest?.Amount.MinorUnits ?? 0,
                latest?.ProviderReference,
                existing.NetRecorded().MinorUnits,
                existing.TotalCompleted().MinorUnits,
                existing.Entries
                    .OrderBy(entry => entry.Sequence)
                    .Select(entry => new RentalRefundCaseEntrySnapshot(
                        entry.Sequence,
                        entry.EntryType,
                        entry.Amount.MinorUnits,
                        entry.ProviderReference,
                        entry.EvidenceReference,
                        entry.OccurredOnUtc))
                    .ToList()));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<RentalRefundCaseSnapshot?>.Failure(RentalRefundErrors.Unknown);
        }
    }

    public async Task<Result<RentalRefundMovementSnapshot>> RecordTimeoutAsync(
        Guid refundCaseId,
        Money ambiguousAmount,
        Guid? actorUserId,
        DateTime timedOutOnUtc,
        string? providerSignal,
        CancellationToken cancellationToken)
    {
        if (refundCaseId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.CaseRequired);
        if (ambiguousAmount.IsNegative)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.AwardInvalid);

        try
        {
            RefundLedger? existing = await LoadCaseAsync(refundCaseId, cancellationToken);
            if (existing is null)
                return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

            RefundLedgerEntry? latest = existing.LatestEntry;
            if (latest is not null && latest.EntryType == RefundLedgerEntryType.TimedOut)
            {
                if (latest.Amount.MinorUnits != ambiguousAmount.MinorUnits)
                    return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

                // The ambiguous outcome is already recorded durably (#17): replay it
                // instead of appending a second timeout the aggregate would refuse.
                return Result<RentalRefundMovementSnapshot>.Success(
                    ToSnapshot(refundCaseId, latest, ReasonFromReference(latest.EvidenceReference), isReplay: true));
            }

            if (latest is null || latest.EntryType != RefundLedgerEntryType.Approved)
                return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

            // The timeout must bind to the case's standing approval — a caller
            // amount that disagrees with the recorded award is refused, so no
            // route can mark a case ambiguous "for" a fabricated amount.
            if (latest.Amount.MinorUnits != ambiguousAmount.MinorUnits)
                return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

            RefundLedgerEntry timedOut = existing.Timeout(
                ambiguousAmount, timedOutOnUtc, actorUserId, providerSignal);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Result<RentalRefundMovementSnapshot>.Success(
                ToSnapshot(refundCaseId, timedOut, ReasonFromReference(latest.EvidenceReference), isReplay: false));
        }
        catch (DomainException ex)
        {
            return Result<RentalRefundMovementSnapshot>.Failure(new Error(RentalRefundErrors.Invalid.Code, ex.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Unknown);
        }
    }

    public async Task<Result<RentalRefundMovementSnapshot>> ReconcileTimeoutAsync(
        Guid refundCaseId,
        Guid actorUserId,
        string evidenceReference,
        string? providerReference,
        DateTime reconciledOnUtc,
        CancellationToken cancellationToken)
    {
        if (refundCaseId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.CaseRequired);
        if (actorUserId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);
        if (string.IsNullOrWhiteSpace(evidenceReference))
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

        try
        {
            RefundLedger? existing = await LoadCaseAsync(refundCaseId, cancellationToken);
            if (existing is null)
                return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

            RefundLedgerEntry? latest = existing.LatestEntry;
            if (latest is not null &&
                latest.EntryType == RefundLedgerEntryType.Failed &&
                latest.Amount.IsZero &&
                existing.Entries.Any(entry => entry.EntryType == RefundLedgerEntryType.TimedOut))
            {
                // Already reconciled: replay the recorded reconciliation.
                return Result<RentalRefundMovementSnapshot>.Success(
                    ToSnapshot(refundCaseId, latest, ReasonFromReference(latest.EvidenceReference), isReplay: true));
            }

            // The aggregate refuses reconciliation of a case with no unresolved
            // timeout — the DomainException maps to the stable Invalid code.
            RefundLedgerEntry reconciled = existing.ReconcileTimeout(
                reconciledOnUtc, actorUserId, evidenceReference, providerReference);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Result<RentalRefundMovementSnapshot>.Success(
                ToSnapshot(refundCaseId, reconciled, ReasonFromReference(reconciled.EvidenceReference), isReplay: false));
        }
        catch (DomainException ex)
        {
            return Result<RentalRefundMovementSnapshot>.Failure(new Error(RentalRefundErrors.Invalid.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Unknown);
        }
    }

    public async Task<Result<RentalRefundMovementSnapshot>> PostRentalRefundCreditAsync(
        Guid refundCaseId,
        Guid familyId,
        Money amount,
        Guid actorUserId,
        DateTime occurredOnUtc,
        string? evidenceReference,
        CancellationToken cancellationToken)
    {
        if (refundCaseId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.CaseRequired);
        if (familyId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);
        if (amount.IsNegative || amount.IsZero)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.AwardInvalid);
        if (actorUserId == Guid.Empty)
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);
        // #16/#31: a zero award completes at its approval entry only — it never
        // reaches the credit posting path — and a movement needs case-level proof.
        if (string.IsNullOrWhiteSpace(evidenceReference))
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Invalid);

        try
        {
            // The case-level proof token binds replay to case + family + amount +
            // evidence: the Rentals-composed evidence (booking, kind, destination,
            // idempotency key) is family-tagged, so a retry under a different
            // family, destination or key can never replay the recorded credit.
            RentalRefundReason reason = ReasonFromReference(evidenceReference);
            string proof = ComposeEvidence(
                reason,
                idempotencyKey: null,
                evidenceReference: $"{evidenceReference!.Trim()};family:{familyId}");

            RefundLedger? existing = await LoadCaseAsync(refundCaseId, cancellationToken);

            if (existing is not null)
            {
                RefundLedgerEntry? postedCredit = existing.Entries
                    .Where(entry => entry.EntryType == RefundLedgerEntryType.CreditBalancePosted)
                    .OrderBy(entry => entry.Sequence)
                    .LastOrDefault();

                if (postedCredit is not null)
                {
                    // Idempotent replay: the credit already landed — replay it only
                    // when case + amount + full proof (family, destination, kind,
                    // key) agree; never touch the balance again either way.
                    bool replayMatches =
                        postedCredit.Amount.MinorUnits == amount.MinorUnits &&
                        string.Equals(postedCredit.EvidenceReference, proof, StringComparison.Ordinal);

                    return replayMatches
                        ? Result<RentalRefundMovementSnapshot>.Success(
                            ToSnapshot(refundCaseId, postedCredit, reason, isReplay: true))
                        : Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.IdempotencyConflict);
                }

                if (FirstMovementEntry(existing) is not null)
                {
                    // The case already stands somewhere else under different content
                    // (e.g. a rails attempt crashed mid-flight) — refusing is the safe
                    // outcome; the resume classification owns recovery.
                    return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.IdempotencyConflict);
                }
            }

            RefundLedger ledger = existing ?? RefundLedger.Open(refundCaseId);
            if (existing is null)
                dbContext.RefundLedgers.Add(ledger);

            CreditBalance balance = await LoadOrCreateBalanceAsync(familyId, cancellationToken);

            // ONE unit of work (true atomicity — the recorder owns the Finance
            // context, so no new interface is needed): the authoritative APPROVED
            // entry, the attributable CreditBalancePosted entry and the balance
            // movement all commit together or not at all. Exactly-once is enforced
            // by the unique (refund case, append sequence) index on the entries
            // plus the balance's concurrency token — a loser's SaveChanges fails
            // whole, and its retry replays the winner's credit entry above (#17).
            ledger.Approve(amount, occurredOnUtc, actorUserId, proof);
            balance.Post(amount, occurredOnUtc);
            ledger.PostCredit(amount, occurredOnUtc, actorUserId, proof);

            await dbContext.SaveChangesAsync(cancellationToken);

            return Result<RentalRefundMovementSnapshot>.Success(
                ToSnapshot(refundCaseId, ledger.LatestEntry!, reason, isReplay: false));
        }
        catch (DomainException ex)
        {
            return Result<RentalRefundMovementSnapshot>.Failure(new Error(RentalRefundErrors.Invalid.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Conflict);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // A raced duplicate lost the append-sequence fence; its retry replays
            // the winner's posted credit entry instead of posting again.
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<RentalRefundMovementSnapshot>.Failure(RentalRefundErrors.Unknown);
        }
    }

    private async Task<CreditBalance> LoadOrCreateBalanceAsync(Guid familyId, CancellationToken cancellationToken)
    {
        var family = new Sanad.BuildingBlocks.Domain.Primitives.Ids.FamilyId(familyId);
        CreditBalance? balance = await dbContext.CreditBalances
            .SingleOrDefaultAsync(candidate => candidate.FamilyId == family, cancellationToken);

        if (balance is not null)
            return balance;

        balance = CreditBalance.Open(family);
        dbContext.CreditBalances.Add(balance);
        return balance;
    }

    /// <summary>True when the latest entry is the zero-amount marker of an already-reconciled timeout, still awaiting the outcome accounting.</summary>
    private static bool IsReconciliationMarker(RefundLedger ledger, RefundLedgerEntry latest) =>
        latest.EntryType == RefundLedgerEntryType.Failed &&
        latest.Amount.IsZero &&
        ledger.Entries.Any(entry => entry.EntryType == RefundLedgerEntryType.TimedOut);

    /// <summary>The latest approved award of a case — the standing exposure while outcome accounting is pending.</summary>
    private static RefundLedgerEntry? LatestAwardEntry(RefundLedger ledger) =>
        ledger.Entries
            .Where(entry => entry.EntryType == RefundLedgerEntryType.Approved)
            .OrderBy(entry => entry.Sequence)
            .LastOrDefault();

    private async Task<RefundLedger?> LoadCaseAsync(Guid refundCaseId, CancellationToken cancellationToken) =>
        await dbContext.RefundLedgers
            .Include(x => x.Entries)
            .SingleOrDefaultAsync(x => x.RefundCaseId == refundCaseId, cancellationToken);

    private static RentalRefundMovementSnapshot ToSnapshot(
        Guid refundCaseId,
        RefundLedgerEntry entry,
        RentalRefundReason reason,
        bool isReplay) =>
        new(
            refundCaseId,
            entry.Amount.MinorUnits,
            Money.CurrencyCode,
            reason,
            isReplay,
            entry.EntryType,
            entry.OccurredOnUtc);

    /// <summary>
    /// Recovers the refund reason from a stored evidence reference. Understands
    /// both the Finance-composed <c>reason:</c> tokens and the Rentals-composed
    /// <c>kind:</c> tokens, so a credit case labelled from the caller's movement
    /// kind replays under its correct reason (M1: cancellation vs early-return
    /// vs deposit are never conflated). Unknown shapes map to the first reason
    /// for snapshot plumbing only.
    /// </summary>
    private static RentalRefundReason ReasonFromReference(string? evidenceReference)
    {
        if (evidenceReference is not null)
        {
            if (evidenceReference.Contains("reason:rental-cancellation-before-delivery", StringComparison.Ordinal) ||
                evidenceReference.Contains("kind:CancellationFullRefund", StringComparison.Ordinal))
                return RentalRefundReason.CancellationBeforeDelivery;
            if (evidenceReference.Contains("reason:rental-early-return-unused-days", StringComparison.Ordinal) ||
                evidenceReference.Contains("kind:EarlyReturnUnusedDays", StringComparison.Ordinal))
                return RentalRefundReason.EarlyReturnUnusedDays;
            if (evidenceReference.Contains("reason:rental-deposit-settlement", StringComparison.Ordinal) ||
                evidenceReference.Contains("kind:DepositSettlement", StringComparison.Ordinal))
                return RentalRefundReason.DepositSettlement;
        }

        return RentalRefundReason.CancellationBeforeDelivery;
    }

    /// <summary>The first money-movement entry of a case, in persisted append order (#35: informational entries never count as movements).</summary>
    private static RefundLedgerEntry? FirstMovementEntry(RefundLedger ledger)
    {
        foreach (RefundLedgerEntry entry in ledger.Entries.OrderBy(entry => entry.Sequence))
        {
            if (entry.EntryType is not (RefundLedgerEntryType.Requested or RefundLedgerEntryType.TransferInitiated))
                return entry;
        }

        return null;
    }

    private static bool HasExpectedShape(RefundLedger ledger, string? providerReference)
    {
        RefundLedgerEntry? latest = ledger.LatestEntry;
        if (latest is null)
            return false;

        // Same-call completion: approval + provider reference means the
        // transfer already confirmed. Without a reference the case stands at
        // approval only — either still in flight, or a completed transfer from
        // a later CompleteTransferAsync call. A recorded timeout is also a
        // matching shape: the case moved beyond the approval and must not be
        // re-approved (the reconciliation gate owns it, #17).
        if (providerReference is not null)
        {
            return latest.EntryType == RefundLedgerEntryType.TransferConfirmed &&
                latest.ProviderReference == providerReference.Trim();
        }

        return latest.EntryType is RefundLedgerEntryType.Approved
            or RefundLedgerEntryType.TransferConfirmed
            or RefundLedgerEntryType.TimedOut;
    }

    internal static string ComposeEvidence(RentalRefundReason reason, string? idempotencyKey, string? evidenceReference)
    {
        string reasonToken = reason switch
        {
            RentalRefundReason.CancellationBeforeDelivery => "reason:rental-cancellation-before-delivery",
            RentalRefundReason.EarlyReturnUnusedDays => "reason:rental-early-return-unused-days",
            RentalRefundReason.DepositSettlement => "reason:rental-deposit-settlement",
            _ => throw new DomainException("A rental refund requires a known reason.")
        };

        string? key = Normalize(idempotencyKey);
        string? supplied = Normalize(evidenceReference);
        if (key is not null)
            reasonToken += $";key:{key}";
        if (supplied is not null)
            reasonToken += $";ref:{supplied}";

        return reasonToken.Length > RefundLedgerEntry.MaximumEvidenceReferenceLength
            ? throw new DomainException("The rental refund evidence reference is too long.")
            : reasonToken;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            string message = current.Message ?? string.Empty;
            if (message.Contains("23505", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("UNIQUE CONSTRAINT FAILED", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Violation of UNIQUE KEY constraint", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
