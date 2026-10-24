using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Finance.Infrastructure;

/// <summary>
/// The Finance-side persistence for duplicate-charge claims. Deliberately typed
/// on the concrete <see cref="FinanceDbContext"/> (the
/// <c>PlatformChargeRuleReader</c> / <c>CaregiverPayoutPolicyStore</c>
/// precedent, registered behind Finance.Application interfaces by the Finance
/// module), because the approval flow must enclose the frozen
/// <see cref="ISubscriptionRefundRecorder"/>'s own <c>SaveChanges</c> — the
/// verdict, the capture-budget reservation and the ledger Approve — in one
/// explicit transaction. A seam this wide needs <c>DbSet&lt;T&gt;</c> plus the
/// relational transaction facade of the concrete context, and the narrower
/// alternative (re-typing <c>IFinanceDbContext</c> to expose
/// <c>Database</c>) would widen the shared application interface for one
/// infrastructure flow. No other module writes this table.
/// <para>
/// Money rules recap: submission never moves money; approval claims the FULL
/// verified basis through the recorder (refused, never trimmed, when the
/// capture cannot fund it); execution persists its intent before the provider
/// call; an unresolved outcome blocks everything but reconciliation.
/// </para>
/// </summary>
public sealed class DuplicateChargeClaimStore(
    FinanceDbContext dbContext,
    ISubscriptionRefundRecorder refunds,
    ISubscriptionRefundGateway gateway,
    IDateTimeProvider clock)
    : IDuplicateChargeClaimStore, IDuplicateChargeClaimReader
{
    public async Task<Result<FamilyDuplicateChargeClaimView>> SubmitAsync(
        DuplicateChargeClaimSubmissionInput input,
        CancellationToken cancellationToken)
    {
        if (input.FamilyAccountId == Guid.Empty || input.SubscriptionId == Guid.Empty ||
            input.PaymentAttemptId == Guid.Empty || input.RefundCaseId == Guid.Empty)
            return Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimErrors.Invalid);
        if (input.BasisMinorUnits <= 0)
            return Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimErrors.Invalid);
        if (input.CustomerReportedOnUtc.Kind != DateTimeKind.Utc || input.SubmittedOnUtc.Kind != DateTimeKind.Utc)
            return Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimErrors.Invalid);

        string? key = NormalizeKey(input.IdempotencyKey);
        try
        {
            // Replay first: the same key re-returns the original claim instead
            // of colliding with the lifetime uniqueness of (family, attempt).
            if (key is not null)
            {
                DuplicateChargeClaim? stored = await dbContext.DuplicateChargeClaims
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        c => c.FamilyAccountId == input.FamilyAccountId && c.IdempotencyKey == key,
                        cancellationToken);
                if (stored is not null)
                    return stored.PaymentAttemptId == input.PaymentAttemptId
                        ? Result<FamilyDuplicateChargeClaimView>.Success(MapFamily(stored, isReplay: true))
                        : Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimErrors.IdempotencyConflict);
            }

            // One LIVE claim per (family, attempt) — the restored R1 partial
            // unique index. A rejected claim stays on record and readable but
            // no longer blocks the family: a fresh submission with a distinct
            // idempotency key and evidence opens a new claim. One money award
            // is still guaranteed by the refund CASE (one Approve entry per
            // case) and the capture budget row, never by this admission count.
            bool liveClaimed = await dbContext.DuplicateChargeClaims
                .AnyAsync(
                    c => c.FamilyAccountId == input.FamilyAccountId &&
                         c.PaymentAttemptId == input.PaymentAttemptId &&
                         c.State != DuplicateChargeClaimState.Rejected,
                    cancellationToken);
            if (liveClaimed)
                return Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimErrors.CaptureAlreadyClaimed);

            // Advisory fast-fail only: the capture budget — shared with legacy
            // awards and shutdown cases — is re-checked authoritatively inside
            // the approval transaction by the recorder + budget guards. The
            // remaining capacity of the capture must still fund the FULL basis;
            // less than that can never be approved, so claim submission refuses
            // with the honest CaptureBasisUnavailable code (mapped to the
            // family's Subscriptions.Refund.ExceedsCaptured) instead of staging
            // a claim doomed at approval time.
            var captureBudget = await dbContext.SubscriptionRefundBudgets
                .Where(b => b.PaymentAttemptId == input.PaymentAttemptId)
                .Select(b => new { b.CapturedMinorUnits, b.ClaimedMinorUnits })
                .SingleOrDefaultAsync(cancellationToken);
            if (captureBudget is not null &&
                captureBudget.CapturedMinorUnits - captureBudget.ClaimedMinorUnits < input.BasisMinorUnits)
            {
                return Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimErrors.CaptureBasisUnavailable);
            }

            DuplicateChargeClaim claim = DuplicateChargeClaim.Submit(
                input.FamilyAccountId,
                input.SubscriptionId,
                input.PaymentAttemptId,
                input.RefundCaseId,
                input.BasisMinorUnits,
                input.CustomerReportedOnUtc,
                input.EvidenceReference,
                key,
                input.SubmittedOnUtc);

            dbContext.DuplicateChargeClaims.Add(claim);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Result<FamilyDuplicateChargeClaimView>.Success(MapFamily(claim, isReplay: false));
        }
        catch (DomainException exception)
        {
            return Result<FamilyDuplicateChargeClaimView>.Failure(
                new Error(DuplicateChargeClaimErrors.Invalid.Code, exception.Message));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // A raced submission lost a uniqueness index. Re-derive the winner:
            // same key → its claim (replay); otherwise the lifetime claim for
            // this (family, attempt); otherwise a genuine conflict.
            if (key is not null)
            {
                DuplicateChargeClaim? byKey = await dbContext.DuplicateChargeClaims
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        c => c.FamilyAccountId == input.FamilyAccountId && c.IdempotencyKey == key,
                        cancellationToken);
                if (byKey is not null)
                    return byKey.PaymentAttemptId == input.PaymentAttemptId
                        ? Result<FamilyDuplicateChargeClaimView>.Success(MapFamily(byKey, isReplay: true))
                        : Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimErrors.IdempotencyConflict);
            }

            bool liveClaimed = await dbContext.DuplicateChargeClaims
                .AnyAsync(
                    c => c.FamilyAccountId == input.FamilyAccountId &&
                         c.PaymentAttemptId == input.PaymentAttemptId &&
                         c.State != DuplicateChargeClaimState.Rejected,
                    cancellationToken);
            return liveClaimed
                ? Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimErrors.CaptureAlreadyClaimed)
                : Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimErrors.Unknown);
        }
    }

    public async Task<Result<DuplicateChargeClaimAdminView>> ApproveAsync(
        Guid claimId,
        Guid decidedByAccountId,
        string? decidedByAccountType,
        DateTime decidedOnUtc,
        string verifiedBasis,
        string? decisionNote,
        string? familyReason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(verifiedBasis))
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Invalid);
        if (decidedOnUtc.Kind != DateTimeKind.Utc)
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Invalid);

        DuplicateChargeClaim? claim = await dbContext.DuplicateChargeClaims
            .SingleOrDefaultAsync(c => c.Id == claimId, cancellationToken);
        if (claim is null)
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.NotFound);
        if (claim.State != DuplicateChargeClaimState.Submitted ||
            claim.ExecutionState != DuplicateChargeExecutionState.None)
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.InvalidState);

        // One explicit transaction encloses the verdict mutation and the
        // recorder's own guarded SaveChanges (budget claim + ledger Approve).
        // Either the claim stays Submitted with no movement, or it becomes
        // Approved with its full-basis reservation and ledger entry committed
        // together.
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            long basis = claim.AwardMinorUnits;

            Result<SubscriptionRefundSnapshot> recorded = await refunds.RecordAsync(
                claim.RefundCaseId,
                claim.FamilyAccountId,
                claim.SubscriptionId,
                claim.PaymentAttemptId,
                basis,
                Money.FromMinorUnits(basis),
                SubscriptionRefundReason.DuplicateCharge,
                idempotencyKey: null,
                decidedOnUtc,
                decidedByAccountId,
                evidenceReference: null,
                cancellationToken);

            if (recorded.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                return Result<DuplicateChargeClaimAdminView>.Failure(MapRecorderError(recorded.Error));
            }

            claim.Approve(
                decidedByAccountId,
                decidedByAccountType,
                decidedOnUtc,
                verifiedBasis,
                decisionNote,
                familyReason);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Result<DuplicateChargeClaimAdminView>.Success(Map(claim));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Conflict);
        }
        catch (DomainException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return Result<DuplicateChargeClaimAdminView>.Failure(
                new Error(DuplicateChargeClaimErrors.Invalid.Code, exception.Message));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Unknown);
        }
    }

    public async Task<Result<DuplicateChargeClaimAdminView>> RejectAsync(
        Guid claimId,
        Guid decidedByAccountId,
        string? decidedByAccountType,
        DateTime decidedOnUtc,
        string familyReason,
        string? decisionNote,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(familyReason))
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Invalid);
        if (decidedOnUtc.Kind != DateTimeKind.Utc)
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Invalid);

        try
        {
            DuplicateChargeClaim? claim = await dbContext.DuplicateChargeClaims
                .SingleOrDefaultAsync(c => c.Id == claimId, cancellationToken);
            if (claim is null)
                return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.NotFound);
            if (claim.State != DuplicateChargeClaimState.Submitted ||
                claim.ExecutionState != DuplicateChargeExecutionState.None)
                return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.InvalidState);

            // Rejection is a single-row verdict: no money moves, nothing to
            // enclose — one tracked mutation, one save.
            claim.Reject(decidedByAccountId, decidedByAccountType, decidedOnUtc, familyReason, decisionNote);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Result<DuplicateChargeClaimAdminView>.Success(Map(claim));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Conflict);
        }
        catch (DomainException exception)
        {
            return Result<DuplicateChargeClaimAdminView>.Failure(
                new Error(DuplicateChargeClaimErrors.Invalid.Code, exception.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Unknown);
        }
    }

    public async Task<Result<DuplicateChargeClaimAdminView>> ExecuteAsync(
        Guid claimId,
        Guid? expectedExecutionAttemptId,
        Guid executedByAccountId,
        string? executedByAccountType,
        DateTime startedOnUtc,
        CancellationToken cancellationToken)
    {
        if (startedOnUtc.Kind != DateTimeKind.Utc)
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Invalid);

        DuplicateChargeClaim? claim = await dbContext.DuplicateChargeClaims
            .SingleOrDefaultAsync(c => c.Id == claimId, cancellationToken);
        if (claim is null)
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.NotFound);

        // Fencing first: a decision prepared for attempt N must never start,
        // settle or clear attempt N+1.
        if (expectedExecutionAttemptId is { } expected &&
            claim.ExecutionAttemptId is { } recorded &&
            expected != recorded)
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.ExecutionAttemptMismatch);

        if (claim.State != DuplicateChargeClaimState.Approved ||
            claim.ExecutionState is not (DuplicateChargeExecutionState.AwaitingExecution or DuplicateChargeExecutionState.Failed))
        {
            // An Ambiguous claim is reconciliation-only: execute is refused
            // until a reconciliation resolves it (Owner-ordered rule), and a
            // Completed claim is already settled.
            return Result<DuplicateChargeClaimAdminView>.Failure(
                claim.ExecutionState == DuplicateChargeExecutionState.Completed
                    ? DuplicateChargeClaimErrors.AlreadySettled
                    : DuplicateChargeClaimErrors.NotExecutable);
        }

        if (claim.ExecutionAttemptCount >= DuplicateChargeClaim.MaximumExecutionAttempts)
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.ExecutableAttemptsExhausted);

        // Fail closed BEFORE any intent is staged: a capture with no resolvable
        // provider transaction never receives an execution attempt, never
        // appends a TransferInitiated, and keeps its state untouched.
        SubscriptionRefundGatewayPreparation preparation =
            await gateway.PrepareAsync(claim.PaymentAttemptId, cancellationToken);
        if (preparation.Status != SubscriptionRefundGatewayPreparationStatus.Ready ||
            string.IsNullOrWhiteSpace(preparation.ChargeTransactionId))
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.ProviderTransactionMissing);

        string chargeTransactionId = preparation.ChargeTransactionId!;

        // E1 — one unit of work, committed BEFORE the provider call: the claim
        // moves to Executing with its new attempt id and award, and the ledger
        // records the instructed transfer with the charge reference. This
        // committed intent is what makes execution exclusive and is what the
        // crash windows reconcile against.
        try
        {
            claim.BeginExecution(executedByAccountId, startedOnUtc);

            RefundLedger? ledger = await GetOrOpenLedgerAsync(claim.RefundCaseId, cancellationToken);
            ledger.InitiateTransfer(
                Money.FromMinorUnits(claim.ExecutionAwardMinorUnits!.Value),
                startedOnUtc,
                executedByAccountId,
                $"paymob:{chargeTransactionId}");

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Conflict);
        }
        catch (DomainException exception)
        {
            // The in-memory claim may hold a partially staged attempt; nothing
            // was committed, and a poisoned tracker must not leak into a later
            // save on this context.
            dbContext.ChangeTracker.Clear();
            return Result<DuplicateChargeClaimAdminView>.Failure(
                new Error(DuplicateChargeClaimErrors.Invalid.Code, exception.Message));
        }

        // E2 — the provider call outside any transaction. An unexpected THROWN
        // gateway exception is exactly as unconfirmable as a 5xx: E1 is already
        // committed, so the money may still be moving and the attempt must be
        // recorded as an unresolved ambiguous outcome, never surfaced as a raw
        // 500 with a misleading plain Executing state. Caller cancellation
        // alone rethrows (a cancelled caller did not create an outcome).
        long award = claim.ExecutionAwardMinorUnits.Value;
        SubscriptionRefundGatewayResult outcome;
        try
        {
            outcome = await gateway.RefundAsync(chargeTransactionId, Money.CurrencyCode, award, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Same mapping as an unexpected gateway result error: the gateway
            // cannot prove the absence of money movement, so reconciliation —
            // never a blind retry — untangles it.
            outcome = SubscriptionRefundGatewayResult.UnknownBecauseReferenceMissing("Paymob.GatewayError");
        }

        // E3 — one unit of work per outcome. The gateway contract maps a
        // success without a refund reference to OutcomeUnknown, so Refunded
        // always carries the provider's own reference. The settle instant is
        // server-authoritative (the provider call consumed wall time the
        // operator-supplied start cannot cover).
        try
        {
            DateTime settledOnUtc = clock.UtcNow;

            switch (outcome.Outcome)
            {
                case SubscriptionRefundGatewayOutcome.Refunded:
                    RefundLedger currentLedger = await GetOrOpenLedgerAsync(claim.RefundCaseId, cancellationToken);
                    // Settled guard: if the case's money is already recorded as
                    // completed, this attempt must not append a second movement.
                    if (currentLedger.TotalCompleted() >= Money.FromMinorUnits(award))
                    {
                        currentLedger.Fail(Money.Zero, settledOnUtc, executedByAccountId);
                        claim.RecordExecutionRefusal(DuplicateChargeClaimErrors.AlreadySettled.Code, settledOnUtc);
                        await dbContext.SaveChangesAsync(cancellationToken);
                        return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.AlreadySettled);
                    }

                    currentLedger.ConfirmTransfer(
                        Money.FromMinorUnits(award),
                        settledOnUtc,
                        executedByAccountId,
                        outcome.ProviderRefundId!,
                        completionEvidenceReference: null);
                    claim.RecordExecutionSuccess(outcome.ProviderRefundId!, settledOnUtc);
                    break;

                case SubscriptionRefundGatewayOutcome.Rejected:
                case SubscriptionRefundGatewayOutcome.NotConfigured:
                    RefundLedger refusedLedger = await GetOrOpenLedgerAsync(claim.RefundCaseId, cancellationToken);
                    // Definitive refusal: provably nothing moved, so the zero
                    // Fail entry records the event and the claim re-arms for a
                    // bounded retry.
                    refusedLedger.Fail(Money.Zero, settledOnUtc, executedByAccountId);
                    claim.RecordExecutionRefusal(outcome.ErrorCode ?? "Paymob.RefundRejected", settledOnUtc);
                    break;

                default:
                    RefundLedger unknownOutcomeLedger = await GetOrOpenLedgerAsync(claim.RefundCaseId, cancellationToken);
                    // The money may or may not have moved. Record the ambiguous
                    // timeout and lock the claim: reconciliation is the only way
                    // forward, exactly like the frozen ledger's unresolved
                    // -timeout gate.
                    unknownOutcomeLedger.Timeout(
                        Money.FromMinorUnits(award),
                        settledOnUtc,
                        executedByAccountId,
                        $"paymob:{chargeTransactionId}");
                    claim.RecordExecutionAmbiguous(outcome.ErrorCode, settledOnUtc);
                    break;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<DuplicateChargeClaimAdminView>.Success(Map(claim));
        }
        catch (DbUpdateConcurrencyException)
        {
            // A late outcome losing a concurrency race to a concurrent
            // reconcile follows the shipped booking rule: a real settlement is
            // never discarded — if the durable claim already records this
            // movement as completed, the late confirmation is an idempotent
            // success; otherwise the caller retries and the ledger's own
            // guards arbitrate.
            DuplicateChargeClaim? persisted = await dbContext.DuplicateChargeClaims
                .AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == claimId, cancellationToken);
            return persisted is { State: DuplicateChargeClaimState.Approved } &&
                   persisted.ExecutionState == DuplicateChargeExecutionState.Completed &&
                   persisted.ExecutionAwardMinorUnits == award
                ? Result<DuplicateChargeClaimAdminView>.Success(Map(persisted))
                : Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Conflict);
        }
        catch (DomainException exception)
        {
            dbContext.ChangeTracker.Clear();
            return Result<DuplicateChargeClaimAdminView>.Failure(
                new Error(DuplicateChargeClaimErrors.Invalid.Code, exception.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            dbContext.ChangeTracker.Clear();
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Unknown);
        }
    }

    public async Task<Result<DuplicateChargeClaimAdminView>> ReconcileAsync(
        DuplicateChargeClaimReconciliationInput input,
        CancellationToken cancellationToken)
    {
        if (input.ReconciledOnUtc.Kind != DateTimeKind.Utc)
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Invalid);

        // Bounded BEFORE anything is mutated, so an oversized note cannot fail
        // after a ledger movement was staged (the shipped booking-reconcile
        // precedent).
        string narrative = input.Evidence.Trim();
        if (narrative.Length == 0)
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.EvidenceRequired);
        if (narrative.Length > RefundLedgerEntry.MaximumEvidenceReferenceLength ||
            (input.MovedEvidenceReference?.Trim().Length ?? 0) > RefundLedgerEntry.MaximumEvidenceReferenceLength ||
            (input.NotMovedProviderEvidenceReference?.Trim().Length ?? 0) > DuplicateChargeClaim.MaximumNotMovedEvidenceLength ||
            (input.VerifiedProviderRefundId?.Trim().Length ?? 0) > RefundLedgerEntry.MaximumProviderReferenceLength)
        {
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Invalid);
        }

        try
        {
            DuplicateChargeClaim? claim = await dbContext.DuplicateChargeClaims
                .SingleOrDefaultAsync(c => c.Id == input.ClaimId, cancellationToken);
            if (claim is null)
                return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.NotFound);

            if (input.ExpectedExecutionAttemptId is { } expected &&
                claim.ExecutionAttemptId is { } recorded &&
                expected != recorded)
                return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.ExecutionAttemptMismatch);

            if (claim.ExecutionState == DuplicateChargeExecutionState.Completed)
                return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.AlreadySettled);

            if (claim.ExecutionState is not (DuplicateChargeExecutionState.Executing or DuplicateChargeExecutionState.Ambiguous))
                return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.InvalidState);

            if (claim.ExecutionAwardMinorUnits is not { } award || award <= 0)
                return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.AwardUnknown);

            RefundLedger ledger = await GetOrOpenLedgerAsync(claim.RefundCaseId, cancellationToken);

            if (claim.ExecutionState == DuplicateChargeExecutionState.Executing)
            {
                // A claim still in flight may still be moving money at the
                // provider: it can never be cleared as "did not move" on an
                // operator's assertion (shipped booking rule, decision #17).
                // It exits only through a verified move.
                if (!input.RefundDidMove)
                    return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.IntentInFlight);

                ledger.ConfirmTransfer(
                    Money.FromMinorUnits(award),
                    input.ReconciledOnUtc,
                    input.ReconciledByAccountId,
                    input.VerifiedProviderRefundId!,
                    input.MovedEvidenceReference?.Trim());
                claim.ReconcileInFlightMoved(input.VerifiedProviderRefundId!, input.ReconciledOnUtc);

                await dbContext.SaveChangesAsync(cancellationToken);
                return Result<DuplicateChargeClaimAdminView>.Success(Map(claim));
            }

            // Ambiguous branch: the ledger carries an unresolved timeout, and
            // its reconciliation is the append the gate waits for.
            if (!ledger.HasUnresolvedTimeout)
                return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.InvalidState);

            claim.ReconcileAmbiguousOutcome(
                input.RefundDidMove,
                input.VerifiedProviderRefundId,
                input.NotMovedProviderEvidenceReference,
                input.ReconciledOnUtc);

            ledger.ReconcileTimeout(
                input.ReconciledOnUtc,
                input.ReconciledByAccountId,
                narrative,
                providerReference: null);

            if (input.RefundDidMove)
            {
                ledger.ConfirmTransfer(
                    Money.FromMinorUnits(award),
                    input.ReconciledOnUtc,
                    input.ReconciledByAccountId,
                    input.VerifiedProviderRefundId!,
                    input.MovedEvidenceReference?.Trim());
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<DuplicateChargeClaimAdminView>.Success(Map(claim));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Conflict);
        }
        catch (DomainException exception)
        {
            dbContext.ChangeTracker.Clear();
            return Result<DuplicateChargeClaimAdminView>.Failure(
                new Error(DuplicateChargeClaimErrors.Invalid.Code, exception.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            dbContext.ChangeTracker.Clear();
            return Result<DuplicateChargeClaimAdminView>.Failure(DuplicateChargeClaimErrors.Unknown);
        }
    }

    public async Task<FamilyDuplicateChargeClaimView?> GetForFamilyAsync(
        Guid familyAccountId,
        Guid claimId,
        CancellationToken cancellationToken)
    {
        DuplicateChargeClaim? claim = await dbContext.DuplicateChargeClaims
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == claimId && c.FamilyAccountId == familyAccountId,
                cancellationToken);

        return claim is null ? null : MapFamily(claim, isReplay: false);
    }

    public async Task<IReadOnlyList<FamilyDuplicateChargeClaimView>> ListForFamilyAsync(
        Guid familyAccountId,
        CancellationToken cancellationToken)
    {
        List<DuplicateChargeClaim> claims = await dbContext.DuplicateChargeClaims
            .AsNoTracking()
            .Where(c => c.FamilyAccountId == familyAccountId)
            .OrderByDescending(c => c.SubmittedOnUtc)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        return claims.Select(c => MapFamily(c, isReplay: false)).ToList();
    }

    public async Task<DuplicateChargeClaimAdminView?> GetAdminAsync(
        Guid claimId,
        CancellationToken cancellationToken)
    {
        DuplicateChargeClaim? claim = await dbContext.DuplicateChargeClaims
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == claimId, cancellationToken);

        return claim is null ? null : Map(claim);
    }

    public async Task<IReadOnlyList<DuplicateChargeClaimAdminView>> ListAdminAsync(
        DuplicateChargeClaimState? state,
        DuplicateChargeExecutionState? executionState,
        Guid? familyAccountId,
        CancellationToken cancellationToken)
    {
        IQueryable<DuplicateChargeClaim> query = dbContext.DuplicateChargeClaims.AsNoTracking();
        if (state is not null)
            query = query.Where(c => c.State == state.Value);
        if (executionState is not null)
            query = query.Where(c => c.ExecutionState == executionState.Value);
        if (familyAccountId is not null && familyAccountId.Value != Guid.Empty)
            query = query.Where(c => c.FamilyAccountId == familyAccountId.Value);

        List<DuplicateChargeClaim> claims = await query
            .OrderByDescending(c => c.SubmittedOnUtc)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        return claims.Select(Map).ToList();
    }

    private async Task<RefundLedger> GetOrOpenLedgerAsync(Guid refundCaseId, CancellationToken cancellationToken)
    {
        RefundLedger? ledger = await dbContext.RefundLedgers
            .Include(l => l.Entries)
            .SingleOrDefaultAsync(l => l.RefundCaseId == refundCaseId, cancellationToken);

        if (ledger is not null)
            return ledger;

        RefundLedger opened = RefundLedger.Open(refundCaseId);
        dbContext.RefundLedgers.Add(opened);
        return opened;
    }

    /// <summary>
    /// Maps the frozen recorder's stable error codes onto the claim workflow's
    /// contract so the API never surfaces recorder internals as a different
    /// failure class.
    /// </summary>
    private static Error MapRecorderError(Error error) => error.Code switch
    {
        // The cross-case cross-channel guard: a movement already exists under
        // this case id (a legacy direct award, or a raced second approve).
        "Finance.SubscriptionRefund.IdempotencyConflict" => DuplicateChargeClaimErrors.AlreadyAwarded,
        // The full-basis rule: the capture cannot fund the entire verified
        // basis, so the approval is refused — never trimmed.
        "Finance.SubscriptionRefund.ExceedsCaptured" => DuplicateChargeClaimErrors.CaptureBasisUnavailable,
        "Finance.SubscriptionRefund.Conflict" => DuplicateChargeClaimErrors.Conflict,
        "Finance.SubscriptionRefund.Unknown" => DuplicateChargeClaimErrors.Unknown,
        _ => new Error(DuplicateChargeClaimErrors.Invalid.Code, error.Message)
    };

    private static FamilyDuplicateChargeClaimView MapFamily(DuplicateChargeClaim claim, bool isReplay) =>
        new(
            claim.Id,
            claim.RefundCaseId,
            claim.PaymentAttemptId,
            claim.SubscriptionId,
            claim.AwardMinorUnits,
            claim.Currency,
            claim.State,
            DuplicateChargeClaimViews.PayoutStatusFor(claim.State, claim.ExecutionState),
            claim.FamilyReason,
            claim.SubmittedOnUtc,
            claim.DecidedOnUtc,
            isReplay);

    private static DuplicateChargeClaimAdminView Map(DuplicateChargeClaim claim) =>
        new(
            claim.Id,
            claim.RefundCaseId,
            claim.FamilyAccountId,
            claim.SubscriptionId,
            claim.PaymentAttemptId,
            claim.AwardMinorUnits,
            claim.Currency,
            claim.State,
            claim.ExecutionState,
            claim.IdempotencyKey,
            claim.EvidenceReference,
            claim.CustomerReportedOnUtc,
            claim.SubmittedOnUtc,
            claim.DecidedByAccountId,
            claim.DecidedByAccountType,
            claim.DecidedOnUtc,
            claim.FamilyReason,
            claim.DecisionNote,
            claim.VerifiedBasis,
            claim.ExecutionAttemptId,
            claim.ExecutedByAccountId,
            claim.ExecutionStartedOnUtc,
            claim.ExecutionAwardMinorUnits,
            claim.GatewayErrorCode,
            claim.ProviderRefundId,
            claim.NotMovedProviderEvidenceReference,
            claim.CompletedOnUtc,
            claim.ExecutionAttemptCount,
            claim.CreatedOnUtc,
            claim.UpdatedOnUtc);

    private static string? NormalizeKey(string? idempotencyKey)
    {
        if (idempotencyKey is null)
            return null;

        string trimmed = idempotencyKey.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            string message = current.Message ?? string.Empty;
            if (message.Contains("23505", StringComparison.OrdinalIgnoreCase) ||
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
