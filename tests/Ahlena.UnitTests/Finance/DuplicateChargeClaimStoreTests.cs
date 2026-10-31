using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Finance.Application;
using Ahlena.Modules.Finance.Domain;
using Ahlena.Modules.Finance.Infrastructure;

namespace Ahlena.UnitTests.Finance;

/// <summary>
/// Support-approval workflow for family duplicate-charge claims: money proofs
/// for the submission → Support verdict → execution → reconciliation pipeline.
/// <para>
/// Shared-cache in-memory SQLite is the only provider available in this suite
/// that enforces relational transactions and concurrency tokens, so the
/// approve transaction (verdict + capture-budget reservation + ledger approve
/// in ONE commit), the execution attempt fencing and the reconciliation gates
/// are exercised at the store boundary (PlatformControlsTests precedent).
/// Real gateway calls are excluded — the seam is a scripted double, and no
/// test ever contacts a provider. Executed in the combined focused suite and
/// full Release suite.
/// </para>
/// </summary>
public sealed class DuplicateChargeClaimStoreTests
{
    private static readonly DateTime BaseUtc = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);

    private static Guid AttemptId(int n) => new($"00000000-0000-0000-0000-{n:000000000000}");

    private static readonly Guid FamilyId = AttemptId(1);
    private static readonly Guid SubscriptionId = AttemptId(2);

    private static DuplicateChargeClaimSubmissionInput SubmissionInput(
        Guid paymentAttemptId,
        long basisMinorUnits,
        string? idempotencyKey = null) =>
        new(
            FamilyId,
            SubscriptionId,
            paymentAttemptId,
            SubscriptionRefundCaseIds.ForDuplicateCharge(paymentAttemptId),
            basisMinorUnits,
            idempotencyKey,
            "screen-x-1",
            BaseUtc,
            BaseUtc);

    [Fact]
    public async Task Submission_creates_a_claim_and_never_moves_money()
    {
        await using Harness harness = await Harness.CreateAsync();
        FakeGateway gateway = FakeGateway.Ready();
        DuplicateChargeClaimStore store = harness.BuildStore(gateway);

        Result<FamilyDuplicateChargeClaimView> submitted = await store.SubmitAsync(
            SubmissionInput(AttemptId(10), basisMinorUnits: 34086, idempotencyKey: "dup-1"),
            default);

        Assert.True(submitted.IsSuccess);
        Assert.Equal(DuplicateChargeClaimState.Submitted, submitted.Value.ClaimState);
        Assert.Equal("pending", submitted.Value.PayoutStatus);
        Assert.Equal(34086L, submitted.Value.EligibleMinorUnits);
        Assert.False(submitted.Value.IsReplay);
        // No ledger, no budget, no award: submission is decision-free and
        // money-free. The recorder owns all money movement.
        Assert.Equal(0, await harness.Db.RefundLedgers.CountAsync());
        Assert.Equal(0, await harness.Db.RefundLedgerEntries.CountAsync());
        Assert.Equal(0, await harness.Db.SubscriptionRefundBudgets.CountAsync());
    }

    [Fact]
    public async Task Submission_replays_the_original_claim_for_the_same_key()
    {
        await using Harness harness = await Harness.CreateAsync();
        DuplicateChargeClaimStore store = harness.BuildStore(FakeGateway.Ready());

        Result<FamilyDuplicateChargeClaimView> first = await store.SubmitAsync(
            SubmissionInput(AttemptId(11), 34086, idempotencyKey: "dup-key"), default);
        Result<FamilyDuplicateChargeClaimView> replay = await store.SubmitAsync(
            SubmissionInput(AttemptId(11), 34086, idempotencyKey: "dup-key"), default);

        Assert.True(first.IsSuccess && replay.IsSuccess);
        Assert.Equal(first.Value.ClaimId, replay.Value.ClaimId);
        Assert.True(replay.Value.IsReplay);
        Assert.Equal(1, await harness.Db.DuplicateChargeClaims.CountAsync());
    }

    [Fact]
    public async Task The_same_key_with_a_different_charge_is_a_conflict()
    {
        await using Harness harness = await Harness.CreateAsync();
        DuplicateChargeClaimStore store = harness.BuildStore(FakeGateway.Ready());

        await store.SubmitAsync(SubmissionInput(AttemptId(12), 34086, idempotencyKey: "dup-key"), default);
        Result<FamilyDuplicateChargeClaimView> conflicted = await store.SubmitAsync(
            SubmissionInput(AttemptId(13), 34086, idempotencyKey: "dup-key"), default);

        Assert.True(conflicted.IsFailure);
        Assert.Equal("Finance.DuplicateClaim.IdempotencyConflict", conflicted.Error.Code);
    }

    [Fact]
    public async Task One_live_claim_per_family_and_capture()
    {
        // Restored R1 admission rule: at most one LIVE (non-rejected) claim
        // per family+attempt. The first claim is live here, so a second live
        // submission for the same capture is refused while it stands.
        await using Harness harness = await Harness.CreateAsync();
        DuplicateChargeClaimStore store = harness.BuildStore(FakeGateway.Ready());

        await store.SubmitAsync(SubmissionInput(AttemptId(14), 34086), default);
        Result<FamilyDuplicateChargeClaimView> second = await store.SubmitAsync(
            SubmissionInput(AttemptId(14), 34086), default);

        Assert.True(second.IsFailure);
        Assert.Equal("Finance.DuplicateClaim.CaptureAlreadyClaimed", second.Error.Code);
    }

    [Fact]
    public async Task A_rejected_claim_can_be_resubmitted_with_new_evidence()
    {
        await using Harness harness = await Harness.CreateAsync();
        DuplicateChargeClaimStore store = harness.BuildStore(FakeGateway.Ready());
        Result<FamilyDuplicateChargeClaimView> first = await store.SubmitAsync(
            SubmissionInput(AttemptId(29), 34086, idempotencyKey: "dup-first"), default);
        Assert.True(first.IsSuccess);

        Result<DuplicateChargeClaimAdminView> rejected = await store.RejectAsync(
            first.Value.ClaimId, AttemptId(90), "SupportAdmin", BaseUtc.AddMinutes(10),
            "The charge was reviewed and is not a duplicate.", null, default);
        Assert.True(rejected.IsSuccess);

        // The original key still replays the rejected original: history, not a
        // new case (replay semantics are unchanged by the restored index).
        Result<FamilyDuplicateChargeClaimView> replay = await store.SubmitAsync(
            SubmissionInput(AttemptId(29), 34086, idempotencyKey: "dup-first"), default);
        Assert.True(replay.IsSuccess);
        Assert.Equal(first.Value.ClaimId, replay.Value.ClaimId);
        Assert.True(replay.Value.IsReplay);
        Assert.Equal(DuplicateChargeClaimState.Rejected, replay.Value.ClaimState);

        // The restored R1 rule: a distinct key + new evidence resubmits the
        // SAME capture. The rejected row keeps no slot; one live claim opens.
        Result<FamilyDuplicateChargeClaimView> resubmitted = await store.SubmitAsync(
            new DuplicateChargeClaimSubmissionInput(
                FamilyId, SubscriptionId, AttemptId(29),
                SubscriptionRefundCaseIds.ForDuplicateCharge(AttemptId(29)),
                34086, "dup-second", "provider statement 8842 confirms double capture",
                BaseUtc.AddMinutes(20), BaseUtc.AddMinutes(20)),
            default);
        Assert.True(resubmitted.IsSuccess);
        Assert.NotEqual(first.Value.ClaimId, resubmitted.Value.ClaimId);
        Assert.False(resubmitted.Value.IsReplay);
        Assert.Equal(DuplicateChargeClaimState.Submitted, resubmitted.Value.ClaimState);
        Assert.Equal("pending", resubmitted.Value.PayoutStatus);

        // Both claims stay readable on the family surface (probe-safe list).
        IReadOnlyList<FamilyDuplicateChargeClaimView> list =
            await store.ListForFamilyAsync(FamilyId, default);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, item => item.ClaimId == first.Value.ClaimId);
        Assert.Contains(list, item => item.ClaimId == resubmitted.Value.ClaimId);

        // The rejected verdict is terminal on ITS claim — no re-decision.
        Result<DuplicateChargeClaimAdminView> lateDecision = await store.ApproveAsync(
            first.Value.ClaimId, AttemptId(90), "SuperAdmin", BaseUtc.AddMinutes(30),
            "Late basis.", null, null, default);
        Assert.True(lateDecision.IsFailure);
        Assert.Equal("Finance.DuplicateClaim.InvalidState", lateDecision.Error.Code);

        // And the one-award money guard holds across resubmission: the fresh
        // claim approves the FULL basis once, under the same refund CASE.
        Result<DuplicateChargeClaimAdminView> approved = await store.ApproveAsync(
            resubmitted.Value.ClaimId, AttemptId(90), "SuperAdmin", BaseUtc.AddMinutes(40),
            "Provider statement 8842 confirms a true double capture.", null, null, default);
        Assert.True(approved.IsSuccess);
        Assert.Equal(34086L, approved.Value.AwardMinorUnits);
    }

    [Fact]
    public async Task The_active_partial_index_refuses_a_concurrent_second_live_claim()
    {
        await using Harness harness = await Harness.CreateAsync();
        DuplicateChargeClaimStore store = harness.BuildStore(FakeGateway.Ready());
        Result<FamilyDuplicateChargeClaimView> first = await store.SubmitAsync(
            SubmissionInput(AttemptId(28), 34086), default);
        Assert.True(first.IsSuccess);

        // The store pre-check is advisory; the partial unique index is the
        // relational backstop. A raced second live claim for the same
        // family+attempt cannot be committed on any context.
        await using FinanceDbContext racer = new(harness.Options);
        racer.DuplicateChargeClaims.Add(DuplicateChargeClaim.Submit(
            FamilyId, SubscriptionId, AttemptId(28),
            SubscriptionRefundCaseIds.ForDuplicateCharge(AttemptId(28)),
            basisMinorUnits: 34086, BaseUtc, "raced-evidence", "raced-key", BaseUtc));

        await Assert.ThrowsAsync<DbUpdateException>(() => racer.SaveChangesAsync());
    }

    [Fact]
    public async Task Submission_refuses_when_the_capture_cannot_fund_the_full_basis()
    {
        await using Harness harness = await Harness.CreateAsync();
        // A legacy/shutdown award already consumed most of this capture. The
        // seed keeps the aggregate reference — queries cannot materialize
        // Added-but-unsaved rows.
        SubscriptionRefundBudget budget = SubscriptionRefundBudget.Open(
            AttemptId(15), SubscriptionId, FamilyId, capturedMinorUnits: 10000, BaseUtc);
        Assert.True(budget.TryClaim(3000, 10000, BaseUtc));
        harness.Db.SubscriptionRefundBudgets.Add(budget);
        await harness.Db.SaveChangesAsync();

        DuplicateChargeClaimStore store = harness.BuildStore(FakeGateway.Ready());
        Result<FamilyDuplicateChargeClaimView> refused = await store.SubmitAsync(
            SubmissionInput(AttemptId(15), 8000), default);

        // NEVER trimmed or staged: the 7000 that remains cannot fund the full
        // 8000 basis, so submission refuses with the honest basis error and
        // nothing is claimed, awarded or written.
        Assert.True(refused.IsFailure);
        Assert.Equal("Finance.DuplicateClaim.CaptureBasisUnavailable", refused.Error.Code);
        Assert.Equal(0, await harness.Db.DuplicateChargeClaims.CountAsync());
        Assert.Equal(0, await harness.Db.RefundLedgerEntries.CountAsync());
        Assert.Equal(3000L,
            (await harness.Db.SubscriptionRefundBudgets.AsNoTracking().SingleAsync()).ClaimedMinorUnits);
    }

    [Fact]
    public async Task Approval_reserves_the_full_basis_atomically_with_the_ledger()
    {
        await using Harness harness = await Harness.CreateAsync();
        DuplicateChargeClaimStore store = harness.BuildStore(FakeGateway.Ready());
        Result<FamilyDuplicateChargeClaimView> submitted = await store.SubmitAsync(
            SubmissionInput(AttemptId(16), 34086), default);
        Assert.True(submitted.IsSuccess);

        Result<DuplicateChargeClaimAdminView> approved = await store.ApproveAsync(
            submitted.Value.ClaimId, AttemptId(90), "SupportAdmin",
            BaseUtc.AddMinutes(30), verifiedBasis: "Full amount re-charged in the same period; verified on provider console.",
            decisionNote: "Screenshots recorded", familyReason: null,
            default);

        Assert.True(approved.IsSuccess);
        Assert.Equal(DuplicateChargeClaimState.Approved, approved.Value.State);
        Assert.Equal(DuplicateChargeExecutionState.AwaitingExecution, approved.Value.ExecutionState);
        Assert.Equal(34086L, approved.Value.AwardMinorUnits);

        // The verdict, the capture reservation and the ledger approve committed
        // together: one budget claim for the FULL basis, one Approve entry.
        SubscriptionRefundBudget budget = await harness.Db.SubscriptionRefundBudgets.AsNoTracking().SingleAsync();
        Assert.Equal(34086L, budget.ClaimedMinorUnits);
        RefundLedger ledger = await harness.Db.RefundLedgers
            .Include(l => l.Entries).AsNoTracking().SingleAsync();
        Assert.Equal(submitted.Value.RefundCaseId, ledger.RefundCaseId);
        Assert.Equal(RefundLedgerEntryType.Approved, ledger.LatestEntry!.EntryType);
        Assert.Equal(1, await harness.Db.RefundLedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Approval_is_refused_when_the_capture_cannot_fund_the_full_basis()
    {
        await using Harness harness = await Harness.CreateAsync();
        // 7000 remains: exactly the full basis, so the submission's advisory
        // fast-fail passes and the claim is legitimately staged. The seed keeps
        // the aggregate reference — queries cannot materialize Added-but-unsaved
        // rows — and it stays tracked, so the drain below is the same row the
        // approval will re-check.
        SubscriptionRefundBudget budget = SubscriptionRefundBudget.Open(
            AttemptId(17), SubscriptionId, FamilyId, capturedMinorUnits: 10000, BaseUtc);
        Assert.True(budget.TryClaim(3000, 10000, BaseUtc));
        harness.Db.SubscriptionRefundBudgets.Add(budget);
        await harness.Db.SaveChangesAsync();

        DuplicateChargeClaimStore store = harness.BuildStore(FakeGateway.Ready());
        Result<FamilyDuplicateChargeClaimView> submitted = await store.SubmitAsync(
            SubmissionInput(AttemptId(17), 7000), default);
        Assert.True(submitted.IsSuccess, "exactly-remaining capacity funds the full basis at submission");

        // The world moves while the claim waits for support: another award
        // consumes 3000, leaving 4000 — less than the 7000 verified basis.
        Assert.True(budget.TryClaim(3000, 10000, BaseUtc.AddMinutes(10)));
        await harness.Db.SaveChangesAsync();

        // NEVER trimmed: the approval transaction re-checks the capture
        // authoritatively (recorder + budget guards in the same unit of work),
        // refuses the full basis, and NOTHING is written — the claim stays
        // Submitted with no ledger movement and no extra reservation.
        Result<DuplicateChargeClaimAdminView> approved = await store.ApproveAsync(
            submitted.Value.ClaimId, AttemptId(90), "SuperAdmin", BaseUtc.AddMinutes(30),
            "Verified duplicate of a fully captured charge.", null, null, default);

        Assert.True(approved.IsFailure);
        Assert.Equal("Finance.DuplicateClaim.CaptureBasisUnavailable", approved.Error.Code);
        Assert.Equal(0, await harness.Db.RefundLedgerEntries.CountAsync());
        DuplicateChargeClaim untouched = await harness.Db.DuplicateChargeClaims.AsNoTracking().SingleAsync();
        Assert.Equal(DuplicateChargeClaimState.Submitted, untouched.State);
        Assert.Equal(6000L,
            (await harness.Db.SubscriptionRefundBudgets.AsNoTracking().SingleAsync()).ClaimedMinorUnits);
    }

    [Fact]
    public async Task Approval_refuses_a_second_award_when_a_legacy_award_exists()
    {
        await using Harness harness = await Harness.CreateAsync();
        DuplicateChargeClaimStore store = harness.BuildStore(FakeGateway.Ready());
        Result<FamilyDuplicateChargeClaimView> submitted = await store.SubmitAsync(
            SubmissionInput(AttemptId(18), 34086), default);

        // The shipping legacy path already awarded this case directly.
        Result<SubscriptionRefundSnapshot> legacy = await new SubscriptionRefundRecorder(harness.Db).RecordAsync(
            submitted.Value.RefundCaseId, FamilyId, SubscriptionId, AttemptId(18),
            capturedMinorUnits: 34086, Money.FromMinorUnits(34086),
            SubscriptionRefundReason.DuplicateCharge,
            idempotencyKey: "legacy-direct-award", BaseUtc, AttemptId(91), null, default);
        Assert.True(legacy.IsSuccess);

        Result<DuplicateChargeClaimAdminView> approved = await store.ApproveAsync(
            submitted.Value.ClaimId, AttemptId(90), "SuperAdmin", BaseUtc.AddMinutes(45),
            "Basis verified.", null, null, default);

        Assert.True(approved.IsFailure);
        Assert.Equal("Finance.DuplicateClaim.AlreadyAwarded", approved.Error.Code);
        Assert.Equal(DuplicateChargeClaimState.Submitted,
            (await harness.Db.DuplicateChargeClaims.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task Rejection_records_a_customer_facing_reason_and_no_money()
    {
        await using Harness harness = await Harness.CreateAsync();
        DuplicateChargeClaimStore store = harness.BuildStore(FakeGateway.Ready());
        Result<FamilyDuplicateChargeClaimView> submitted = await store.SubmitAsync(
            SubmissionInput(AttemptId(19), 34086), default);

        Result<DuplicateChargeClaimAdminView> rejected = await store.RejectAsync(
            submitted.Value.ClaimId, AttemptId(90), "SupportAdmin", BaseUtc.AddMinutes(10),
            "The charge was reviewed and is not a duplicate.", null, default);

        Assert.True(rejected.IsSuccess);
        Assert.Equal(DuplicateChargeClaimState.Rejected, rejected.Value.State);
        Assert.Equal(0, await harness.Db.RefundLedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Execution_confirms_the_transfer_and_completes_the_claim()
    {
        await using Harness harness = await Harness.CreateAsync();
        FakeGateway gateway = FakeGateway.Refunded("rfn-abc");
        DuplicateChargeClaimStore store = harness.BuildStore(gateway);
        Guid claimId = (await harness.SeedApprovedAsync(AttemptId(20), 34086, store)).ClaimId;

        Result<DuplicateChargeClaimAdminView> executed = await store.ExecuteAsync(
            claimId, expectedExecutionAttemptId: null, AttemptId(92), "SuperAdmin", BaseUtc.AddHours(1), default);

        Assert.True(executed.IsSuccess);
        Assert.Equal(DuplicateChargeExecutionState.Completed, executed.Value.ExecutionState);
        Assert.Equal("rfn-abc", executed.Value.ProviderRefundId);
        Assert.Single(gateway.RefundAmounts);
        Assert.Equal(34086L, gateway.RefundAmounts[0]);
        RefundLedger ledger = await harness.Db.RefundLedgers
            .Include(l => l.Entries).AsNoTracking().SingleAsync();
        Assert.Equal(RefundLedgerEntryType.TransferConfirmed, ledger.LatestEntry!.EntryType);
        Assert.Equal("rfn-abc", ledger.LatestEntry.ProviderReference);
        Assert.Equal(34086L, ledger.TotalCompleted().MinorUnits);
    }

    [Fact]
    public async Task Execution_refusal_fails_the_attempt_and_re_arms_a_bounded_retry()
    {
        await using Harness harness = await Harness.CreateAsync();
        FakeGateway gateway = FakeGateway.Rejected();
        DuplicateChargeClaimStore store = harness.BuildStore(gateway);
        Guid claimId = (await harness.SeedApprovedAsync(AttemptId(21), 34086, store)).ClaimId;

        Result<DuplicateChargeClaimAdminView> first = await store.ExecuteAsync(
            claimId, null, AttemptId(92), "SuperAdmin", BaseUtc.AddHours(1), default);

        Assert.True(first.IsSuccess);
        Assert.Equal(DuplicateChargeExecutionState.Failed, first.Value.ExecutionState);
        Assert.Equal(1, first.Value.ExecutionAttemptCount);
        Assert.Equal("Paymob.RefundRejected", first.Value.GatewayErrorCode);

        Guid recordedAttempt = first.Value.ExecutionAttemptId!.Value;
        // A decision prepared for attempt 1 must never carry attempt 2.
        Result<DuplicateChargeClaimAdminView> fenced = await store.ExecuteAsync(
            claimId, expectedExecutionAttemptId: Guid.NewGuid(), AttemptId(92), "SuperAdmin",
            BaseUtc.AddHours(2), default);
        Assert.True(fenced.IsFailure);
        Assert.Equal("Finance.DuplicateClaim.ExecutionAttemptMismatch", fenced.Error.Code);

        Result<DuplicateChargeClaimAdminView> retry = await store.ExecuteAsync(
            claimId, recordedAttempt, AttemptId(92), "SuperAdmin", BaseUtc.AddHours(2), default);
        Assert.True(retry.IsSuccess);
        Assert.Equal(2, retry.Value.ExecutionAttemptCount);
        // The refusal is provable, so the ledger records a zero-amount failure:
        // no money ever moved (Owner decision #17).
        RefundLedger ledger = await harness.Db.RefundLedgers
            .Include(l => l.Entries).AsNoTracking().SingleAsync();
        Assert.Equal(0L, ledger.TotalCompleted().MinorUnits);
    }

    [Fact]
    public async Task An_unconfirmed_outcome_blocks_execution_until_reconciled()
    {
        await using Harness harness = await Harness.CreateAsync();
        FakeGateway gateway = FakeGateway.Unknown();
        DuplicateChargeClaimStore store = harness.BuildStore(gateway);
        Guid claimId = (await harness.SeedApprovedAsync(AttemptId(22), 34086, store)).ClaimId;

        Result<DuplicateChargeClaimAdminView> executed = await store.ExecuteAsync(
            claimId, null, AttemptId(92), "SuperAdmin", BaseUtc.AddHours(1), default);

        Assert.True(executed.IsSuccess);
        Assert.Equal(DuplicateChargeExecutionState.Ambiguous, executed.Value.ExecutionState);
        // The money may or may not have moved: the ledger gate is the
        // unresolved timeout, and execute is refused outright.
        RefundLedger ledger = await harness.Db.RefundLedgers
            .Include(l => l.Entries).AsNoTracking().SingleAsync();
        Assert.True(ledger.HasUnresolvedTimeout);

        Result<DuplicateChargeClaimAdminView> retryRefused = await store.ExecuteAsync(
            claimId, executed.Value.ExecutionAttemptId, AttemptId(92), "SuperAdmin",
            BaseUtc.AddHours(2), default);
        Assert.True(retryRefused.IsFailure);
        Assert.Equal("Finance.DuplicateClaim.NotExecutable", retryRefused.Error.Code);
    }

    [Fact]
    public async Task Execution_fails_closed_when_no_provider_transaction_is_resolvable()
    {
        await using Harness harness = await Harness.CreateAsync();
        DuplicateChargeClaimStore store = harness.BuildStore(FakeGateway.Missing());
        Guid claimId = (await harness.SeedApprovedAsync(AttemptId(23), 34086, store)).ClaimId;

        Result<DuplicateChargeClaimAdminView> executed = await store.ExecuteAsync(
            claimId, null, AttemptId(92), "SuperAdmin", BaseUtc.AddHours(1), default);

        Assert.True(executed.IsFailure);
        Assert.Equal("Finance.DuplicateClaim.ProviderTransactionMissing", executed.Error.Code);
        DuplicateChargeClaim untouched = await harness.Db.DuplicateChargeClaims.AsNoTracking().SingleAsync();
        Assert.Equal(DuplicateChargeClaimState.Approved, untouched.State);
        Assert.Equal(DuplicateChargeExecutionState.AwaitingExecution, untouched.ExecutionState);
        Assert.Equal(0, untouched.ExecutionAttemptCount);

        // The approval's recorded award survives; the failed preparation must
        // not append ANY execution movement — no initiation, no confirmation,
        // and no money recorded as completed.
        RefundLedger ledger = await harness.Db.RefundLedgers
            .Include(l => l.Entries).AsNoTracking().SingleAsync();
        RefundLedgerEntry soleEntry = Assert.Single(ledger.Entries);
        Assert.Equal(RefundLedgerEntryType.Approved, soleEntry.EntryType);
        Assert.Equal(0L, ledger.TotalCompleted().MinorUnits);
    }

    [Fact]
    public async Task A_thrown_gateway_exception_is_an_unresolved_ambiguous_outcome()
    {
        await using Harness harness = await Harness.CreateAsync();
        FakeGateway gateway = FakeGateway.Throwing();
        DuplicateChargeClaimStore store = harness.BuildStore(gateway);
        Guid claimId = (await harness.SeedApprovedAsync(AttemptId(30), 34086, store)).ClaimId;

        // The gateway THROWS after E1 committed (a transport crash the client
        // library escalates as an exception). No exception may escape to the
        // operator as a raw 500, and the durable attempt must record the same
        // unconfirmable outcome as a lost 5xx — never a plain Executing row.
        Result<DuplicateChargeClaimAdminView> executed = await store.ExecuteAsync(
            claimId, null, AttemptId(92), "SuperAdmin", BaseUtc.AddHours(1), default);

        Assert.True(executed.IsSuccess);
        Assert.Equal(DuplicateChargeExecutionState.Ambiguous, executed.Value.ExecutionState);
        Assert.Equal("Paymob.GatewayError", executed.Value.GatewayErrorCode);

        RefundLedger ledger = await harness.Db.RefundLedgers
            .Include(l => l.Entries).AsNoTracking().SingleAsync();
        Assert.True(ledger.HasUnresolvedTimeout);

        // No retry until reconciled — the unresolved-timeout gate holds.
        Result<DuplicateChargeClaimAdminView> retry = await store.ExecuteAsync(
            claimId, executed.Value.ExecutionAttemptId, AttemptId(92), "SuperAdmin",
            BaseUtc.AddHours(2), default);
        Assert.True(retry.IsFailure);
        Assert.Equal("Finance.DuplicateClaim.NotExecutable", retry.Error.Code);

        // Reconciliation with affirmative, operator-verified NotMoved evidence
        // is the only way forward.
        Guid attemptId = executed.Value.ExecutionAttemptId!.Value;
        Result<DuplicateChargeClaimAdminView> reconciled = await store.ReconcileAsync(
            new DuplicateChargeClaimReconciliationInput(
                claimId, attemptId, RefundDidMove: false,
                VerifiedProviderRefundId: null, MovedEvidenceReference: null,
                NotMovedProviderEvidenceReference: "provider-case-9911: no refund exists on txn",
                Evidence: "Provider enquiry 9911 (operator-verified) states no refund was issued.",
                AttemptId(92), "SuperAdmin", BaseUtc.AddHours(3)),
            default);
        Assert.True(reconciled.IsSuccess);
        Assert.Equal(DuplicateChargeExecutionState.Failed, reconciled.Value.ExecutionState);
    }

    [Fact]
    public async Task A_caller_cancellation_rethrows_and_leaves_the_committed_intent()
    {
        await using Harness harness = await Harness.CreateAsync();
        DuplicateChargeClaimStore store = harness.BuildStore(FakeGateway.Cancelled());
        Guid claimId = (await harness.SeedApprovedAsync(AttemptId(31), 34086, store)).ClaimId;

        // A cancelled CALLER is not an unconfirmable provider outcome: the
        // cancellation rethrows while the committed E1 intent stays honestly
        // in flight and reconcilable.
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.ExecuteAsync(claimId, null, AttemptId(92), "SuperAdmin", BaseUtc.AddHours(1), default));

        DuplicateChargeClaim claim = await harness.Db.DuplicateChargeClaims.AsNoTracking().SingleAsync();
        Assert.Equal(DuplicateChargeExecutionState.Executing, claim.ExecutionState);
        Assert.Null(claim.GatewayErrorCode);
        Assert.NotNull(claim.ExecutionAttemptId);
    }

    [Fact]
    public async Task An_in_flight_attempt_can_never_be_reconciled_as_not_moved()
    {
        await using Harness harness = await Harness.CreateAsync();
        // A genuinely in-flight provider call, made deterministic: the seeded
        // cancellation is exactly a client abandoning an E2 call that may still
        // land at the provider, leaving E1's committed intent honestly
        // Executing with its attempt id.
        DuplicateChargeClaimStore store = harness.BuildStore(FakeGateway.Cancelled());
        Guid claimId = (await harness.SeedApprovedAsync(AttemptId(24), 34086, store)).ClaimId;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.ExecuteAsync(claimId, null, AttemptId(92), "SuperAdmin", BaseUtc.AddHours(1), default));

        DuplicateChargeClaim inFlight = await harness.Db.DuplicateChargeClaims.AsNoTracking().SingleAsync();
        Assert.Equal(DuplicateChargeExecutionState.Executing, inFlight.ExecutionState);
        Assert.NotNull(inFlight.ExecutionAttemptId);

        // The operator sees Executing and asks for a "did not move" verdict,
        // affirmative evidence in hand. The provider call may still land, so
        // the store refuses on an assertion alone (shipped rule #17): nothing
        // is recorded, and the attempt stays in flight.
        Result<DuplicateChargeClaimAdminView> reconciled = await store.ReconcileAsync(
            new DuplicateChargeClaimReconciliationInput(
                claimId, inFlight.ExecutionAttemptId,
                RefundDidMove: false,
                VerifiedProviderRefundId: null,
                MovedEvidenceReference: null,
                NotMovedProviderEvidenceReference: "provider statement ref says no refund",
                Evidence: "Provider statement 2314 confirms no refund exists.",
                AttemptId(92), "SuperAdmin", BaseUtc.AddHours(2)),
            default);

        Assert.True(reconciled.IsFailure);
        Assert.Equal("Finance.DuplicateClaim.IntentInFlight", reconciled.Error.Code);
        // The refusal recorded nothing: the attempt stays honestly in flight.
        Assert.Equal(DuplicateChargeExecutionState.Executing,
            (await harness.Db.DuplicateChargeClaims.AsNoTracking().SingleAsync()).ExecutionState);
    }

    [Fact]
    public async Task An_ambiguous_not_moved_reconciliation_requires_affirmative_provider_evidence()
    {
        await using Harness harness = await Harness.CreateAsync();
        FakeGateway gateway = FakeGateway.Unknown();
        DuplicateChargeClaimStore store = harness.BuildStore(gateway);
        Guid claimId = (await harness.SeedApprovedAsync(AttemptId(25), 34086, store)).ClaimId;
        await store.ExecuteAsync(claimId, null, AttemptId(92), "SuperAdmin", BaseUtc.AddHours(1), default);
        Guid attemptId = (await harness.Db.DuplicateChargeClaims.AsNoTracking().SingleAsync())
            .ExecutionAttemptId!.Value;

        // The acceptance correction: mere absence of a refund at a point in
        // time is NOT evidence. Without the affirmative provider evidence
        // reference the reconciliation cannot claim "not moved".
        Result<DuplicateChargeClaimAdminView> withoutEvidence = await store.ReconcileAsync(
            new DuplicateChargeClaimReconciliationInput(
                claimId, attemptId, RefundDidMove: false,
                VerifiedProviderRefundId: null, MovedEvidenceReference: null,
                NotMovedProviderEvidenceReference: null,
                Evidence: "No refund visible on the provider console at 14:02.",
                AttemptId(92), "SuperAdmin", BaseUtc.AddHours(3)),
            default);

        Assert.True(withoutEvidence.IsFailure);
        Assert.Equal(DuplicateChargeExecutionState.Ambiguous,
            (await harness.Db.DuplicateChargeClaims.AsNoTracking().SingleAsync()).ExecutionState);

        Result<DuplicateChargeClaimAdminView> withEvidence = await store.ReconcileAsync(
            new DuplicateChargeClaimReconciliationInput(
                claimId, attemptId, RefundDidMove: false,
                VerifiedProviderRefundId: null, MovedEvidenceReference: null,
                NotMovedProviderEvidenceReference: "provider-case-8842: no refund exists on txn",
                Evidence: "Provider enquiry 8842 (operator-verified) states no refund was issued.",
                AttemptId(92), "SuperAdmin", BaseUtc.AddHours(4)),
            default);

        Assert.True(withEvidence.IsSuccess);
        DuplicateChargeClaim settled = await harness.Db.DuplicateChargeClaims.AsNoTracking().SingleAsync();
        Assert.Equal(DuplicateChargeExecutionState.Failed, settled.ExecutionState);
        Assert.Equal("provider-case-8842: no refund exists on txn", settled.NotMovedProviderEvidenceReference);
        // The payout remains reserved: the claim may be retried bounded, never
        // re-approved (the award stayed claimed the whole time).
        SubscriptionRefundBudget budget = await harness.Db.SubscriptionRefundBudgets.AsNoTracking().SingleAsync();
        Assert.Equal(34086L, budget.ClaimedMinorUnits);
        // Boundedly re-executable after the affirmative NotMoved settlement.
        Result<DuplicateChargeClaimAdminView> retrigger = await store.ExecuteAsync(
            claimId, settled.ExecutionAttemptId, AttemptId(92), "SuperAdmin", BaseUtc.AddHours(5), default);
        Assert.True(retrigger.IsSuccess);
    }

    [Fact]
    public async Task An_ambiguous_moved_reconciliation_completes_with_the_provider_reference()
    {
        await using Harness harness = await Harness.CreateAsync();
        FakeGateway gateway = FakeGateway.Unknown();
        DuplicateChargeClaimStore store = harness.BuildStore(gateway);
        Guid claimId = (await harness.SeedApprovedAsync(AttemptId(26), 34086, store)).ClaimId;
        await store.ExecuteAsync(claimId, null, AttemptId(92), "SuperAdmin", BaseUtc.AddHours(1), default);
        Guid attemptId = (await harness.Db.DuplicateChargeClaims.AsNoTracking().SingleAsync())
            .ExecutionAttemptId!.Value;

        Result<DuplicateChargeClaimAdminView> reconciled = await store.ReconcileAsync(
            new DuplicateChargeClaimReconciliationInput(
                claimId, attemptId, RefundDidMove: true,
                VerifiedProviderRefundId: "rfn-late-777",
                MovedEvidenceReference: "provider statement 8841 confirms the refund",
                NotMovedProviderEvidenceReference: null,
                Evidence: "Provider statement 8841 confirms the refund landed 13:55.",
                AttemptId(92), "SuperAdmin", BaseUtc.AddHours(3)),
            default);

        Assert.True(reconciled.IsSuccess);
        Assert.Equal(DuplicateChargeExecutionState.Completed, reconciled.Value.ExecutionState);
        RefundLedger ledger = await harness.Db.RefundLedgers
            .Include(l => l.Entries).AsNoTracking().SingleAsync();
        Assert.False(ledger.HasUnresolvedTimeout);
        Assert.Equal(34086L, ledger.TotalCompleted().MinorUnits);

        // Settled: a second attempt can never add a second movement.
        Result<DuplicateChargeClaimAdminView> second = await store.ExecuteAsync(
            claimId, attemptId, AttemptId(92), "SuperAdmin", BaseUtc.AddHours(4), default);
        Assert.True(second.IsFailure);
        Assert.Equal("Finance.DuplicateClaim.AlreadySettled", second.Error.Code);
    }

    [Fact]
    public async Task Reconciliation_is_fenced_to_the_attempt_it_was_prepared_for()
    {
        await using Harness harness = await Harness.CreateAsync();
        FakeGateway gateway = FakeGateway.Unknown();
        DuplicateChargeClaimStore store = harness.BuildStore(gateway);
        Guid claimId = (await harness.SeedApprovedAsync(AttemptId(27), 34086, store)).ClaimId;
        await store.ExecuteAsync(claimId, null, AttemptId(92), "SuperAdmin", BaseUtc.AddHours(1), default);
        Guid attemptId = (await harness.Db.DuplicateChargeClaims.AsNoTracking().SingleAsync())
            .ExecutionAttemptId!.Value;

        Result<DuplicateChargeClaimAdminView> fenced = await store.ReconcileAsync(
            new DuplicateChargeClaimReconciliationInput(
                claimId, Guid.NewGuid(), RefundDidMove: true,
                VerifiedProviderRefundId: "rfn-late-778", MovedEvidenceReference: null,
                NotMovedProviderEvidenceReference: null,
                Evidence: "Prepared for a stale attempt.",
                AttemptId(92), "SuperAdmin", BaseUtc.AddHours(3)),
            default);

        Assert.True(fenced.IsFailure);
        Assert.Equal("Finance.DuplicateClaim.ExecutionAttemptMismatch", fenced.Error.Code);
        _ = attemptId;
    }

    [Fact]
    public async Task Reconcile_handler_refuses_a_not_moved_verdict_without_affirmative_evidence()
    {
        // Bounded handler test: the affirmative-evidence rule is enforced
        // BEFORE any store call, so an operator can never clear an attempt on
        // a point-in-time absence.
        RecordingStore store = new();
        var handler = new ReconcileDuplicateChargeClaimCommandHandler(store);

        Result<DuplicateChargeClaimAdminView> refused = await handler.Handle(
            new ReconcileDuplicateChargeClaimCommand(
                Guid.NewGuid(), null, RefundDidMove: false,
                VerifiedProviderRefundId: null, MovedEvidenceReference: null,
                NotMovedProviderEvidenceReference: null,
                Evidence: "Nothing visible right now.",
                AttemptId(92), "SuperAdmin", BaseUtc),
            default);

        Assert.True(refused.IsFailure);
        Assert.Equal("Finance.DuplicateClaim.NotMovedEvidenceRequired", refused.Error.Code);
        Assert.Equal(0, store.ReconcileCalls);
    }

    [Fact]
    public async Task The_seam_role_split_keeps_decisions_reviewer_only_and_money_executor_only()
    {
        RecordingStore store = new();

        // SupportAdmin may decide; FinanceAdmin may not (policy mirror).
        Result<DuplicateChargeClaimAdminView> support = await new ApproveDuplicateChargeClaimCommandHandler(store)
            .Handle(new ApproveDuplicateChargeClaimCommand(
                Guid.NewGuid(), AttemptId(93), "SupportAdmin", BaseUtc, "basis"), default);
        Assert.True(support.IsSuccess);

        Result<DuplicateChargeClaimAdminView> finance = await new ApproveDuplicateChargeClaimCommandHandler(store)
            .Handle(new ApproveDuplicateChargeClaimCommand(
                Guid.NewGuid(), AttemptId(93), "FinanceAdmin", BaseUtc, "basis"), default);
        Assert.True(finance.IsFailure);
        Assert.Equal("Finance.DuplicateClaim.ReviewerRequired", finance.Error.Code);

        // Execution and reconciliation are SuperAdmin-only: a SupportAdmin
        // token passed the HTTP policy would still be refused in the seam.
        Result<DuplicateChargeClaimAdminView> supportExecute = await new ExecuteDuplicateChargeClaimCommandHandler(store)
            .Handle(new ExecuteDuplicateChargeClaimCommand(
                Guid.NewGuid(), null, AttemptId(93), "SupportAdmin", BaseUtc), default);
        Assert.True(supportExecute.IsFailure);
        Assert.Equal("Finance.DuplicateClaim.ExecutorRequired", supportExecute.Error.Code);

        await new ExecuteDuplicateChargeClaimCommandHandler(store)
            .Handle(new ExecuteDuplicateChargeClaimCommand(
                Guid.NewGuid(), null, AttemptId(93), "SuperAdmin", BaseUtc), default);
        Assert.Equal(1, store.ExecuteCalls);
    }

    /// <summary>SQLite harness: shared-cache keep-alive, char_length shim, one context.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _keepAlive;

        private Harness(FinanceDbContext db, DbContextOptions<FinanceDbContext> options, SqliteConnection keepAlive)
        {
            Db = db;
            Options = options;
            _keepAlive = keepAlive;
        }

        public FinanceDbContext Db { get; }

        /// <summary>Shared options for spawning raced second contexts on the same in-memory database.</summary>
        public DbContextOptions<FinanceDbContext> Options { get; }

        public DuplicateChargeClaimStore BuildStore(ISubscriptionRefundGateway gateway) =>
            new(Db, new SubscriptionRefundRecorder(Db), gateway, new FixedClock(BaseUtc.AddMinutes(5)));

        public async Task<FamilyDuplicateChargeClaimView> SeedApprovedAsync(
            Guid paymentAttemptId, long basisMinorUnits, DuplicateChargeClaimStore store)
        {
            Result<FamilyDuplicateChargeClaimView> submitted = await store.SubmitAsync(
                SubmissionInput(paymentAttemptId, basisMinorUnits), default);
            Assert.True(submitted.IsSuccess);
            Result<DuplicateChargeClaimAdminView> approved = await store.ApproveAsync(
                submitted.Value.ClaimId, AttemptId(90), "SuperAdmin", BaseUtc.AddMinutes(30),
                "Basis verified against the provider console.", null, null, default);
            Assert.True(approved.IsSuccess);
            return submitted.Value;
        }

        public static async Task<Harness> CreateAsync()
        {
            string connectionString =
                $"Data Source=file:duplicate_claims_{Guid.NewGuid():N};mode=memory;cache=shared";
            SqliteConnection keepAlive = new(connectionString);
            await keepAlive.OpenAsync();
            // The Finance model carries PostgreSQL check constraints that use
            // char_length; register the portable equivalent (E04 precedent).
            keepAlive.CreateFunction("char_length", (string? value) => value?.Length ?? 0);

            DbContextOptions<FinanceDbContext> options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlite(keepAlive)
                .Options;

            FinanceDbContext db = new(options);
            await db.Database.EnsureCreatedAsync();
            return new Harness(db, options, keepAlive);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _keepAlive.DisposeAsync();
        }
    }

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed class FakeGateway : ISubscriptionRefundGateway
    {
        private enum GatewayBehavior
        {
            Normal,
            ThrowUnexpected,
            ThrowCancellation
        }

        private FakeGateway(
            SubscriptionRefundGatewayPreparation preparation,
            SubscriptionRefundGatewayResult outcome,
            GatewayBehavior behavior = GatewayBehavior.Normal)
        {
            Preparation = preparation;
            NextOutcome = outcome;
            Behavior = behavior;
        }

        public SubscriptionRefundGatewayPreparation Preparation { get; }

        public SubscriptionRefundGatewayResult NextOutcome { get; }

        private GatewayBehavior Behavior { get; }

        public List<long> RefundAmounts { get; } = [];

        public static FakeGateway Ready() => new(
            SubscriptionRefundGatewayPreparation.WithChargeTransaction("txn-charge-1"),
            SubscriptionRefundGatewayResult.UnknownBecauseReferenceMissing("unset"));

        public static FakeGateway Missing() => new(
            SubscriptionRefundGatewayPreparation.Missing(),
            SubscriptionRefundGatewayResult.UnknownBecauseReferenceMissing("unset"));

        public static FakeGateway Throwing() => new(
            SubscriptionRefundGatewayPreparation.WithChargeTransaction("txn-charge-1"),
            SubscriptionRefundGatewayResult.UnknownBecauseReferenceMissing("unset"),
            GatewayBehavior.ThrowUnexpected);

        public static FakeGateway Cancelled() => new(
            SubscriptionRefundGatewayPreparation.WithChargeTransaction("txn-charge-1"),
            SubscriptionRefundGatewayResult.UnknownBecauseReferenceMissing("unset"),
            GatewayBehavior.ThrowCancellation);

        public static FakeGateway Refunded(string providerRefundId) => new(
            SubscriptionRefundGatewayPreparation.WithChargeTransaction("txn-charge-1"),
            new SubscriptionRefundGatewayResult(
                SubscriptionRefundGatewayOutcome.Refunded, providerRefundId, null));

        public static FakeGateway Rejected() => new(
            SubscriptionRefundGatewayPreparation.WithChargeTransaction("txn-charge-1"),
            new SubscriptionRefundGatewayResult(
                SubscriptionRefundGatewayOutcome.Rejected, null, "Paymob.RefundRejected"));

        public static FakeGateway Unknown() => new(
            SubscriptionRefundGatewayPreparation.WithChargeTransaction("txn-charge-1"),
            SubscriptionRefundGatewayResult.UnknownBecauseReferenceMissing("Paymob.RefundOutcomeUnknown"));

        public Task<SubscriptionRefundGatewayPreparation> PrepareAsync(
            Guid paymentAttemptId, CancellationToken cancellationToken) =>
            Task.FromResult(Preparation);

        public Task<SubscriptionRefundGatewayResult> RefundAsync(
            string chargeTransactionId, string currency, long awardMinorUnits,
            CancellationToken cancellationToken)
        {
            RefundAmounts.Add(awardMinorUnits);
            return Behavior switch
            {
                GatewayBehavior.ThrowUnexpected =>
                    throw new InvalidOperationException("gateway socket exploded mid-call"),
                GatewayBehavior.ThrowCancellation => throw new OperationCanceledException(),
                _ => Task.FromResult(NextOutcome)
            };
        }
    }

    /// <summary>Records seam calls so handler guards can be asserted without a database.</summary>
    private sealed class RecordingStore : IDuplicateChargeClaimStore
    {
        public int ExecuteCalls { get; private set; }

        public int ReconcileCalls { get; private set; }

        public Task<Result<FamilyDuplicateChargeClaimView>> SubmitAsync(
            DuplicateChargeClaimSubmissionInput input, CancellationToken cancellationToken) =>
            Task.FromResult(Result<FamilyDuplicateChargeClaimView>.Success(new FamilyDuplicateChargeClaimView(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100,
                "EGP", DuplicateChargeClaimState.Submitted, "pending", null, BaseUtc, null, false)));

        public Task<Result<DuplicateChargeClaimAdminView>> ApproveAsync(
            Guid claimId, Guid decidedByAccountId, string? decidedByAccountType,
            DateTime decidedOnUtc, string verifiedBasis, string? decisionNote,
            string? familyReason, CancellationToken cancellationToken) =>
            Task.FromResult(Result<DuplicateChargeClaimAdminView>.Success(AdminView(claimId)));

        public Task<Result<DuplicateChargeClaimAdminView>> RejectAsync(
            Guid claimId, Guid decidedByAccountId, string? decidedByAccountType,
            DateTime decidedOnUtc, string familyReason, string? decisionNote,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<DuplicateChargeClaimAdminView>.Success(AdminView(claimId)));

        public Task<Result<DuplicateChargeClaimAdminView>> ExecuteAsync(
            Guid claimId, Guid? expectedExecutionAttemptId, Guid executedByAccountId,
            string? executedByAccountType, DateTime startedOnUtc, CancellationToken cancellationToken)
        {
            ExecuteCalls++;
            return Task.FromResult(Result<DuplicateChargeClaimAdminView>.Success(AdminView(claimId)));
        }

        public Task<Result<DuplicateChargeClaimAdminView>> ReconcileAsync(
            DuplicateChargeClaimReconciliationInput input, CancellationToken cancellationToken)
        {
            ReconcileCalls++;
            return Task.FromResult(Result<DuplicateChargeClaimAdminView>.Success(AdminView(input.ClaimId)));
        }

        private static DuplicateChargeClaimAdminView AdminView(Guid claimId) =>
            new(claimId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100, "EGP",
                DuplicateChargeClaimState.Submitted, DuplicateChargeExecutionState.None, null, null,
                BaseUtc, BaseUtc, null, null, null, null, null, null, null, null, null, null, null,
                null, null, null, 0, BaseUtc, BaseUtc);
    }
}
